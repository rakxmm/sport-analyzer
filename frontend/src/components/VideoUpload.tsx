import { useEffect, useRef, useState } from 'react'
import { formatBytes } from '../lib/format'
import type { Match } from '../lib/types'
import { ApiError, useApi } from '../lib/useApi'
import { uploadVideo, videoContentType } from '../lib/videoUpload'

type UploadStatus = 'idle' | 'uploading' | 'paused' | 'completing' | 'error'

interface Props {
  match: Match
  onMatchChange: (match: Match) => void
}

/** Resumable upload of the raw match video straight to R2 (FR-6, FR-7, FR-8). */
export function VideoUpload({ match, onMatchChange }: Props) {
  const api = useApi()
  const [file, setFile] = useState<File | null>(null)
  const [status, setStatus] = useState<UploadStatus>('idle')
  const [uploadedBytes, setUploadedBytes] = useState(0)
  const [error, setError] = useState<string | null>(null)
  const controllerRef = useRef<AbortController | null>(null)
  const runningRef = useRef<Promise<void> | null>(null)

  const isBusy = status === 'uploading' || status === 'completing'

  useEffect(() => {
    if (!isBusy) return
    const warn = (event: BeforeUnloadEvent) => event.preventDefault()
    window.addEventListener('beforeunload', warn)
    return () => window.removeEventListener('beforeunload', warn)
  }, [isBusy])

  // Leaving the page pauses the upload; R2 keeps the finished parts for resuming later.
  useEffect(() => () => controllerRef.current?.abort(), [])

  function handleFileChange(event: React.ChangeEvent<HTMLInputElement>) {
    const selected = event.target.files?.[0] ?? null
    setError(null)
    setUploadedBytes(0)
    setStatus('idle')
    if (selected && !videoContentType(selected)) {
      setFile(null)
      setError('Please choose a video file.')
      return
    }
    setFile(selected)
  }

  function startOrResume() {
    runningRef.current = runUpload()
  }

  async function runUpload() {
    if (!file) return
    const controller = new AbortController()
    controllerRef.current = controller
    setError(null)
    setStatus('uploading')

    try {
      const updated = await uploadVideo(api, match.id, file, {
        signal: controller.signal,
        onProgress: setUploadedBytes,
        onCompleting: () => setStatus('completing'),
      })
      setStatus('idle')
      setFile(null)
      onMatchChange(updated)
    } catch (err) {
      if (controller.signal.aborted) {
        setStatus('paused')
        return
      }
      setStatus('error')
      setError(err instanceof Error ? err.message : 'Upload failed')
    } finally {
      if (controllerRef.current === controller) controllerRef.current = null
    }
  }

  function pause() {
    controllerRef.current?.abort()
  }

  async function cancel() {
    if (!window.confirm('Cancel this upload? Parts uploaded so far will be discarded.')) return
    controllerRef.current?.abort()
    // Let the paused upload settle first, so a start request still in flight can't recreate the upload after we cancel it.
    await runningRef.current
    try {
      onMatchChange(await api.abortVideoUpload(match.id))
    } catch (err) {
      // 400 means there was no upload left to cancel on the server — the end state is the same.
      if (!(err instanceof ApiError && err.status === 400)) {
        setError(err instanceof Error ? err.message : 'Failed to cancel upload')
        return
      }
    }
    setFile(null)
    setStatus('idle')
    setUploadedBytes(0)
    setError(null)
  }

  const totalBytes = file?.size ?? match.videoSizeBytes ?? 0
  const percent = totalBytes > 0 ? Math.floor((uploadedBytes / totalBytes) * 100) : 0
  const showProgress = status !== 'idle' || uploadedBytes > 0

  return (
    <div className="card">
      <h2>Upload match video</h2>

      {match.uploadInProgress && !file && match.videoFileName && (
        <p className="notice">
          The upload of <strong>{match.videoFileName}</strong>
          {match.videoSizeBytes !== null && <> ({formatBytes(match.videoSizeBytes)})</>} was interrupted. Choose
          the same file again to continue where it stopped.
        </p>
      )}

      <input type="file" accept="video/*" disabled={isBusy || status === 'paused'} onChange={handleFileChange} />

      {showProgress && (
        <div className="upload-progress">
          <progress value={uploadedBytes} max={totalBytes || 1} />
          <span>
            {status === 'completing'
              ? 'Finishing upload…'
              : `${formatBytes(uploadedBytes)} of ${formatBytes(totalBytes)} (${percent} %)`}
          </span>
        </div>
      )}

      {error && <p className="error">{error}</p>}

      <div className="button-row">
        {status === 'uploading' && (
          <button type="button" onClick={pause}>
            Pause
          </button>
        )}
        {(status === 'idle' || status === 'paused' || status === 'error') && file && (
          <button type="button" onClick={startOrResume}>
            {status === 'idle' ? 'Upload' : 'Resume'}
          </button>
        )}
        {(match.uploadInProgress || status !== 'idle') && status !== 'completing' && (
          <button type="button" onClick={cancel}>
            Cancel upload
          </button>
        )}
      </div>
    </div>
  )
}
