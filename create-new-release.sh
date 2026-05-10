#!/usr/bin/env bash
set -euo pipefail

usage() {
  echo "Usage: $(basename "$0") [--allow-republish] <x.y.z>" >&2
  echo "  Updates version strings in source (MainWindow.xaml footer; Noted.csproj <Version> and" >&2
  echo "  <AssemblyInformationalVersion>), runs publish, commits, and adds git tag v<x.y.z>." >&2
  echo "  New version must be greater than <Version> in Noted.csproj unless --allow-republish" >&2
  echo "  is used with an explicit x.y.z to rebuild that same version (moves tag v<x.y.z> if set)." >&2
  exit 1
}

ALLOW_REPUBLISH=0
POSITIONAL=()
for arg in "$@"; do
  case "$arg" in
    --allow-republish)
      ALLOW_REPUBLISH=1
      ;;
    *)
      POSITIONAL+=("$arg")
      ;;
  esac
done

NEW="${POSITIONAL[0]:-}"
if [ "${#POSITIONAL[@]}" -gt 1 ]; then
  usage
fi
if [ -z "${NEW}" ]; then
  if [ "${ALLOW_REPUBLISH}" -eq 1 ]; then
    echo "error: --allow-republish must be used with a version tag (x.y.z)" >&2
  else
    usage
  fi
  exit 1
fi
# Strip CR if version was pasted from Windows clipboard
NEW="${NEW//$'\r'/}"

if ! [[ "${NEW}" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
  echo "error: version must look like x.y.z (e.g. 0.13.0)" >&2
  exit 1
fi

ROOT="$(cd "$(dirname "$0")" && pwd)"
cd "${ROOT}"

OLD="$(grep -m1 '<Version>' Noted.csproj | sed 's/.*<Version>\([^<]*\)<\/Version>.*/\1/' | tr -d '\r')"
if [ -z "${OLD}" ]; then
  echo "error: could not read <Version> from Noted.csproj" >&2
  exit 1
fi

SAME_VERSION=0
if [ "${NEW}" = "${OLD}" ]; then
  SAME_VERSION=1
  if [ "${ALLOW_REPUBLISH}" -ne 1 ]; then
    echo "error: new version (${NEW}) must not equal current version (${OLD}) (use --allow-republish to republish)" >&2
    exit 1
  fi
else
  # Lexicographic version sort: smaller version sorts first
  first="$(printf '%s\n' "${OLD}" "${NEW}" | sort -V | head -1)"
  if [ "${first}" = "${NEW}" ]; then
    echo "error: new version (${NEW}) must be greater than current version (${OLD})" >&2
    exit 1
  fi
fi

TAG="v${NEW}"
FORCE_PUSH_TAG=0
if git rev-parse "${TAG}" >/dev/null 2>&1; then
  if [ "${ALLOW_REPUBLISH}" -eq 1 ] && [ "${SAME_VERSION}" -eq 1 ]; then
    FORCE_PUSH_TAG=1
  else
    echo "error: tag ${TAG} already exists" >&2
    exit 1
  fi
fi

if ! grep -q "Text=\"${OLD}\"" MainWindow.xaml; then
  echo "error: MainWindow.xaml has no Text=\"${OLD}\" — align the footer version with Noted.csproj <Version> (${OLD}) before releasing." >&2
  exit 1
fi

echo "Updating version strings in source code: MainWindow.xaml; Noted.csproj (<Version>, <AssemblyInformationalVersion>) → ${NEW}"

sed -i "s|<Version>.*</Version>|<Version>${NEW}</Version>|" Noted.csproj
sed -i "s|<AssemblyInformationalVersion>.*</AssemblyInformationalVersion>|<AssemblyInformationalVersion>${NEW}</AssemblyInformationalVersion>|" Noted.csproj
sed -i "s|Text=\"${OLD}\"|Text=\"${NEW}\"|" MainWindow.xaml

bash "${ROOT}/publish-win-x64.sh"

if [ ! -f "${ROOT}/dist/win-x64/Noted.exe" ]; then
  echo "error: publish did not produce dist/win-x64/Noted.exe" >&2
  exit 1
fi

git add MainWindow.xaml Noted.csproj dist/win-x64/Noted.exe
git commit -m "Publish binary for Noted ${NEW}"
if [ "${FORCE_PUSH_TAG}" -eq 1 ]; then
  git tag -f "${TAG}"
else
  git tag "${TAG}"
fi

echo
echo "Last 5 commits:"
git log -5 --oneline --decorate
echo
if [ "${FORCE_PUSH_TAG}" -eq 1 ]; then
  echo "Republish: remote tag must be updated."
  echo "  git push"
  echo "  git push --force-with-lease origin ${TAG}"
else
  echo "Push the commit and tag:"
  echo "  git push --follow-tags"
fi
echo
