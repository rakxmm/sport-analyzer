import { useAuth } from '@clerk/clerk-react'
import { useCallback } from 'react'
import type { Clip, Match, Tag } from './types'

const API_BASE_URL = import.meta.env.VITE_API_BASE_URL

class ApiError extends Error {
  status: number

  constructor(status: number, message: string) {
    super(message)
    this.status = status
  }
}

/** Authenticated fetch against the ASP.NET Core API, attaching the current Clerk session token. */
export function useApi() {
  const { getToken } = useAuth()

  const request = useCallback(
    async <T,>(path: string, init?: RequestInit): Promise<T> => {
      const token = await getToken()
      const response = await fetch(`${API_BASE_URL}${path}`, {
        ...init,
        headers: {
          'Content-Type': 'application/json',
          ...(token ? { Authorization: `Bearer ${token}` } : {}),
          ...init?.headers,
        },
      })

      if (!response.ok) {
        const body = await response.json().catch(() => ({ detail: response.statusText }))
        throw new ApiError(response.status, body.detail ?? response.statusText)
      }

      if (response.status === 204) {
        return undefined as T
      }

      return (await response.json()) as T
    },
    [getToken],
  )

  return {
    getMatches: () => request<Match[]>('/api/matches/'),
    getMatch: (id: string) => request<Match>(`/api/matches/${id}`),
    createMatch: (title: string, matchDate: string, consentConfirmed: boolean) =>
      request<Match>('/api/matches/', {
        method: 'POST',
        body: JSON.stringify({ title, matchDate, consentConfirmed }),
      }),

    getTags: (matchId: string) => request<Tag[]>(`/api/matches/${matchId}/tags/`),
    createTag: (
      matchId: string,
      tag: { type: Tag['type']; timestampSeconds: number; leadInSeconds: number; leadOutSeconds: number; note?: string },
    ) =>
      request<Tag>(`/api/matches/${matchId}/tags/`, {
        method: 'POST',
        body: JSON.stringify(tag),
      }),
    deleteTag: (tagId: string) => request<void>(`/api/tags/${tagId}`, { method: 'DELETE' }),

    getClips: (matchId: string) => request<Clip[]>(`/api/matches/${matchId}/clips/`),
    createClip: (matchId: string, type: Clip['type'], tagIds: string[]) =>
      request<Clip>(`/api/matches/${matchId}/clips/`, {
        method: 'POST',
        body: JSON.stringify({ type, tagIds }),
      }),
  }
}
