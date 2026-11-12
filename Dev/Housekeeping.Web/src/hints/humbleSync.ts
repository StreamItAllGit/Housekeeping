// @module: Housekeeping.Web/hints/humbleSync
// @purpose: Standard 09 2-line hover hints for Module 6 (Humble Bundle Library Sync) controls.
// @depends-on: none
// @exports: HUMBLE_SYNC_HINTS
export const HUMBLE_SYNC_HINTS = {
  sessionCookie:
    'Authenticates humble-bundle-downloader against your Humble library (_simpleauth_sess).\nStored locally in settings.json and redacted from diagnostic bundles.',
  libraryPath:
    'Destination root where purchased eBooks, comics, audio, and DRM-free games download.\nIncremental runs skip already-downloaded items via hbd cache.',
} as const;
