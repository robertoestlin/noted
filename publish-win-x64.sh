#!/usr/bin/env bash
set -euo pipefail

PROJECT="Noted.csproj"
RID="win-x64"
CONFIG="Release"
OUTDIR="dist/${RID}"
BASE_EXE="Noted.exe"

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

echo
echo "Done. Folder output:"
echo "  ${OUTDIR}"
echo
echo "Main executable:"
echo "  ${OUTDIR}/${BASE_EXE}"
