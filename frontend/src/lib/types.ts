export type VideoStatus = 'PendingUpload' | 'Uploaded' | 'Processing' | 'Ready' | 'Failed'
export type TagType = 'Goal' | 'Assist' | 'Save' | 'Foul' | 'Other'
export type ClipType = 'SingleClip' | 'HighlightReel'
export type ClipStatus = 'Queued' | 'Processing' | 'Done' | 'Failed'

export interface Match {
  id: string
  title: string
  matchDate: string
  videoStatus: VideoStatus
  durationSeconds: number | null
  createdAt: string
}

export interface Tag {
  id: string
  matchId: string
  type: TagType
  timestampSeconds: number
  leadInSeconds: number
  leadOutSeconds: number
  note: string | null
}

export interface Clip {
  id: string
  matchId: string
  type: ClipType
  status: ClipStatus
  shareToken: string
  shareExpiresAt: string | null
}
