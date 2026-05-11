#!/usr/bin/env bash
set -euo pipefail

PROJECT="Heartbeat.csproj"
RID="win-x64"
CONFIG="Release"
OUTDIR="../dist/${RID}"
BASE_EXE="Heartbeat.exe"
OUT_BIN="Heartbeat.bin"

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

echo "Copying ${BASE_EXE} → ${OUT_BIN} ..."
cp -f "${OUTDIR}/${BASE_EXE}" "${OUTDIR}/${OUT_BIN}"

echo
echo "Done. Folder output:"
echo "  ${OUTDIR}"
echo
echo "Executables:"
echo "  ${OUTDIR}/${BASE_EXE}"
echo "  ${OUTDIR}/${OUT_BIN}"
