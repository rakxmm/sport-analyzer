import type { Match, PartUploadUrl, UploadSession } from './types'
import { ApiError } from './useApi'

/** The subset of useApi() the uploader needs. */
export interface VideoUploadApi {
  startVideoUpload: (
    matchId: string,
    file: { fileName: string; contentType: string; sizeBytes: number },
  ) => Promise<UploadSession>
  getPartUploadUrl: (matchId: string, partNumber: number) => Promise<PartUploadUrl>
  completeVideoUpload: (matchId: string) => Promise<Match>
}

interface UploadOptions {
  /** Aborting pauses the upload; calling uploadVideo again with the same file resumes it. */
  signal: AbortSignal
  onProgress: (uploadedBytes: number, totalBytes: number) => void
}

const CONCURRENT_PARTS = 4
const RETRY_DELAYS_MS = [1_000, 2_000, 4_000, 8_000, 16_000, 30_000]

/**
 * Uploads the raw match video straight to R2 as a multipart upload (FR-6, FR-7). The server reports
 * which parts R2 already holds, so a paused, dropped or reloaded upload continues where it stopped.
 */
export async function uploadVideo(api: VideoUploadApi, matchId: string, file: File, options: UploadOptions): Promise<Match> {
  const session = await api.startVideoUpload(matchId, {
    fileName: file.name,
    contentType: file.type,
    sizeBytes: file.size,
  })

  const partBounds = (partNumber: number) => {
    const start = (partNumber - 1) * session.partSizeBytes
    return { start, end: Math.min(start + session.partSizeBytes, file.size) }
  }
  const partSize = (partNumber: number) => {
    const { start, end } = partBounds(partNumber)
    return end - start
  }

  const completed = new Set(session.completedPartNumbers)
  const pending = Array.from({ length: session.partCount }, (_, i) => i + 1).filter((n) => !completed.has(n))

  let completedBytes = session.completedPartNumbers.reduce((sum, n) => sum + partSize(n), 0)
  const inFlightBytes = new Map<number, number>()
  const reportProgress = () => {
    let inFlight = 0
    inFlightBytes.forEach((bytes) => (inFlight += bytes))
    options.onProgress(completedBytes + inFlight, file.size)
  }
  reportProgress()

  // One permanently failed part stops the other workers too.
  const controller = new AbortController()
  const stop = () => controller.abort(options.signal.reason)
  options.signal.addEventListener('abort', stop)

  const uploadPart = async (partNumber: number) => {
    const { start, end } = partBounds(partNumber)
    const blob = file.slice(start, end)

    for (let attempt = 0; ; attempt++) {
      try {
        // Signed right before sending, so a long pause never leaves us holding an expired URL.
        const { url } = await api.getPartUploadUrl(matchId, partNumber)
        await putBlob(url, blob, controller.signal, (loaded) => {
          inFlightBytes.set(partNumber, loaded)
          reportProgress()
        })
        inFlightBytes.delete(partNumber)
        completedBytes += blob.size
        reportProgress()
        return
      } catch (error) {
        inFlightBytes.delete(partNumber)
        reportProgress()
        if (controller.signal.aborted || attempt >= RETRY_DELAYS_MS.length || isPermanent(error)) throw error
        await waitUntilOnline(controller.signal)
        await delay(RETRY_DELAYS_MS[attempt], controller.signal)
      }
    }
  }

  const worker = async () => {
    for (let partNumber = pending.shift(); partNumber !== undefined; partNumber = pending.shift()) {
      await uploadPart(partNumber)
    }
  }

  try {
    await Promise.all(
      Array.from({ length: Math.min(CONCURRENT_PARTS, pending.length) }, () =>
        worker().catch((error) => {
          controller.abort(error)
          throw error
        }),
      ),
    )
  } finally {
    options.signal.removeEventListener('abort', stop)
  }

  options.signal.throwIfAborted()
  return api.completeVideoUpload(matchId)
}

/** The API refused the request itself (e.g. the upload was cancelled elsewhere) — retrying won't help. */
function isPermanent(error: unknown): boolean {
  return error instanceof ApiError && error.status >= 400 && error.status < 500 && error.status !== 408 && error.status !== 429
}

/** PUT via XHR because fetch() does not report upload progress. */
function putBlob(url: string, blob: Blob, signal: AbortSignal, onProgress: (loaded: number) => void): Promise<void> {
  return new Promise((resolve, reject) => {
    if (signal.aborted) {
      reject(signal.reason)
      return
    }

    const xhr = new XMLHttpRequest()
    const onAbort = () => xhr.abort()
    signal.addEventListener('abort', onAbort)
    const cleanup = () => signal.removeEventListener('abort', onAbort)

    xhr.open('PUT', url)
    xhr.upload.onprogress = (event) => onProgress(event.loaded)
    xhr.onload = () => {
      cleanup()
      if (xhr.status >= 200 && xhr.status < 300) resolve()
      else reject(new Error(`Storage rejected part upload (HTTP ${xhr.status}).`))
    }
    xhr.onerror = () => {
      cleanup()
      reject(new Error('Network error while uploading.'))
    }
    xhr.onabort = () => {
      cleanup()
      reject(signal.reason ?? new DOMException('Upload paused.', 'AbortError'))
    }
    xhr.send(blob)
  })
}

function delay(ms: number, signal: AbortSignal): Promise<void> {
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => {
      signal.removeEventListener('abort', onAbort)
      resolve()
    }, ms)
    const onAbort = () => {
      clearTimeout(timer)
      reject(signal.reason)
    }
    signal.addEventListener('abort', onAbort, { once: true })
  })
}

/** Offline time doesn't count against the retry budget — the upload just waits for the connection. */
function waitUntilOnline(signal: AbortSignal): Promise<void> {
  if (navigator.onLine) return Promise.resolve()
  return new Promise((resolve, reject) => {
    const onOnline = () => {
      signal.removeEventListener('abort', onAbort)
      resolve()
    }
    const onAbort = () => {
      window.removeEventListener('online', onOnline)
      reject(signal.reason)
    }
    window.addEventListener('online', onOnline, { once: true })
    signal.addEventListener('abort', onAbort, { once: true })
  })
}
