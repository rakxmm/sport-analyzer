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
  videoFileName: string | null
  videoSizeBytes: number | null
  uploadInProgress: boolean
  createdAt: string
}

export interface UploadSession {
  matchId: string
  fileName: string
  sizeBytes: number
  partSizeBytes: number
  partCount: number
  completedPartNumbers: number[]
}

export interface PartUploadUrl {
  partNumber: number
  url: string
}

export interface VideoUrl {
  url: string
  expiresAt: string
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
