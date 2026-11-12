// @module: Housekeeping.Web/hints/archiveCompressor
// @purpose: Standard 09 2-line hover hints for Module 3 (Video Archive Compressor) controls and table headers.
// @depends-on: none
// @exports: ARCHIVE_COMPRESSOR_HINTS
export const ARCHIVE_COMPRESSOR_HINTS = {
  mappingEnabled:
    'Enables source-to-archive video compression for this folder pair.\nDefers automatically if StreamEngine or Redroom holds an active .hklock.',
  thEnabled:
    'Controls whether this source→archive folder pair runs when the job executes.\nUncheck to pause archiving for a project folder.',
  thLabel:
    'Friendly display name for this archive mapping in terminal logs.\nHelps identify project roots during batch runs.',
  thSourceFolder:
    'Working video directory scanned recursively for uncompressed/master footage.\nSubfolder structure is mirrored inside the destination.',
  thDestFolder:
    'Long-term storage directory where compressed HEVC/AV1 files are committed.\nUses safe exclusive FileShare.None pre-probing before atomic .partial swaps.',
  thAction:
    'Removes this folder mapping from the job configuration.\nExisting archived videos on disk are never deleted.',
} as const;
