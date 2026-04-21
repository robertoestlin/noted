#!/usr/bin/env bash
set -euo pipefail

PROJECT="Heartbeat.csproj"
RID="win-x64"
CONFIG="Release"
OUTDIR="../dist/${RID}"
BASE_EXE="Heartbeat.exe"

echo "Publishing ${PROJECT} to ${OUTDIR} ..."

mkdir -p "${OUTDIR}"

dotnet publish "${PROJECT}" \
  -c "${CONFIG}" \
  -r "${RID}" \
  --self-contained true \
  -p:OutputType=WinExe \
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
