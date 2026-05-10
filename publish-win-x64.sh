#!/usr/bin/env bash
set -euo pipefail

# Copy General documentation package for repo/GitHub (maintainer machine: default Noted backup path).
# Source: canonical on-disk name (.docp = zip of package.json + pages + images).
GENERAL_DOC_SRC="c:/tools/backup/noted/doc-packages/noted-general.docp"

PROJECT="Noted.csproj"
RID="win-x64"
CONFIG="Release"
OUTDIR="dist/${RID}"
BASE_EXE="Noted.exe"

ROOT="$(cd "$(dirname "$0")" && pwd)"

echo "Publishing ${PROJECT} to ${OUTDIR} ..."

mkdir -p "${OUTDIR}"
echo "Removing previous ${BASE_EXE} from ${OUTDIR} ..."
rm -f "${OUTDIR}/${BASE_EXE}"

dotnet publish "${PROJECT}" \
  -c "${CONFIG}" \
  -r "${RID}" \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:PublishReadyToRun=false \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:IncludeAllContentForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true \
  -p:DebugType=None \
  -p:DebugSymbols=false \
  -o "${OUTDIR}"

echo
echo "Done. Folder output:"
echo "  ${OUTDIR}"
echo
echo "Main executable:"
echo "  ${OUTDIR}/${BASE_EXE}"

DOC_PKG_DIR="${ROOT}/dist/doc-packages"
DOC_PKG_DEST="${DOC_PKG_DIR}/noted-general.docp"
mkdir -p "${DOC_PKG_DIR}"
if [ ! -f "${GENERAL_DOC_SRC}" ]; then
  echo "error: General doc package not found at ${GENERAL_DOC_SRC}" >&2
  exit 1
fi
# Remove the previous .json copy so the dist folder doesn't keep stale formats.
rm -f "${DOC_PKG_DIR}/noted-general.doc-package.json"
cp -f "${GENERAL_DOC_SRC}" "${DOC_PKG_DEST}"
echo
echo "Copied General doc package to:"
echo "  ${DOC_PKG_DEST}"
