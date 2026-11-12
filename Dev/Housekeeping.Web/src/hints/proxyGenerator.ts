// @module: Housekeeping.Web/hints/proxyGenerator
// @purpose: Standard 09 2-line hover hints for Module 2 (Proxy Media Generator) controls and table headers.
// @depends-on: none
// @exports: PROXY_GENERATOR_HINTS
export const PROXY_GENERATOR_HINTS = {
  folderEnabled:
    'Enables recursive proxy media scanning for this footage directory.\nAutomatically defers when StreamEngine holds an active .hklock on the folder.',
  thEnabled:
    'Controls whether this footage folder is scanned during Proxy Generator runs.\nDisabled rows stay saved without spawning encodes.',
  thFolderPath:
    'Local capture directory or NAS share scanned for raw master clips.\nExisting proxy files matching the Proxy Suffix Tag are skipped automatically.',
  thAction:
    'Removes this target folder from the Proxy Generator job.\nDoes not delete any generated proxy files on disk.',
} as const;
