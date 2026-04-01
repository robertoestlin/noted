#!/usr/bin/env bash
set -euo pipefail

PROJECT="Noted.csproj"
RID="win-x64"
CONFIG="Release"
OUTDIR="dist/${RID}"
DATE_STAMP="${DATE_STAMP:-$(date +%Y%m%d)}"
BASE_EXE="Noted.exe"
DATED_EXE="Noted-${DATE_STAMP}.exe"

echo "Publishing ${PROJECT} to ${OUTDIR} ..."

echo "Removing ${OUTDIR} folder..."
rm -rf "${OUTDIR}"
mkdir -p "${OUTDIR}"

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

if [[ -f "${OUTDIR}/${BASE_EXE}" ]]; then
  mv -f "${OUTDIR}/${BASE_EXE}" "${OUTDIR}/${DATED_EXE}"
fi

echo
echo "Done. Folder output:"
echo "  ${OUTDIR}"
echo
echo "Main executable:"
echo "  ${OUTDIR}/${DATED_EXE}"
