#!/usr/bin/env bash
# Decides which areas of the repo a pull request touches, so a workflow runs only the jobs that
# matter (ci.yml, sdks.yml). Reads area rules on stdin, one per line:
#
#   <area> <pattern> [<pattern> ...]
#
# A pattern is a bash pattern matched against the whole path, where `*` also matches `/`, so
# `server/*` means everything under server/. A pattern starting with `!` excludes; an area with
# only exclusions matches any file none of them match. Two names are special: files matching
# `ignore` count for no area (Markdown, which no build or test reads), and when `all` matches,
# every area is true (use it for the workflow file itself and this script).
#
# Writes `<area>=true|false` to $GITHUB_OUTPUT and a table to the job summary. On any event other
# than pull_request (a push to main, a release, a manual run) every area is true.
#
# Usage: changed-areas.sh <base> <head> < rules
set -euo pipefail
set -f # patterns are matched by hand, never expanded against the checkout

base=$1
head=$2
out=${GITHUB_OUTPUT:-/dev/stdout}
summary=${GITHUB_STEP_SUMMARY:-/dev/null}

areas=()
rules=()
while read -r area patterns; do
  [[ -z $area || $area == \#* ]] && continue
  areas+=("$area")
  rules+=("$patterns")
done

if [[ ${GITHUB_EVENT_NAME:-pull_request} != pull_request ]]; then
  for area in "${areas[@]}"; do [[ $area == all || $area == ignore ]] || echo "$area=true" >> "$out"; done
  echo "Every job runs on a ${GITHUB_EVENT_NAME} event." >> "$summary"
  exit 0
fi

ignore=
for i in "${!areas[@]}"; do [[ ${areas[$i]} == ignore ]] && ignore=${rules[$i]}; done

# ignored <file>: true when the file matches an `ignore` pattern.
ignored() {
  local p
  for p in $ignore; do [[ $1 == $p ]] && return 0; done
  return 1
}

# Read into a variable first: a failed diff must fail the job, never look like "nothing changed".
changed=$(git diff --name-only "$base" "$head")
files=()
while IFS= read -r f; do
  [[ -n $f ]] && ! ignored "$f" && files+=("$f")
done <<< "$changed"

# matches <patterns> <file>: true when the file is in the area.
matches() {
  local include=false positive=false p
  for p in $1; do
    if [[ $p == !* ]]; then
      [[ $2 == ${p:1} ]] && return 1
    else
      positive=true
      [[ $2 == $p ]] && include=true
    fi
  done
  [[ $include == true || $positive == false ]]
}

# hit[i] is true when area i matches a changed file (indexed, so this also runs on macOS bash 3.2).
hit=()
everything=false
for i in "${!areas[@]}"; do
  hit[i]=false
  for f in ${files[@]+"${files[@]}"}; do
    if matches "${rules[$i]}" "$f"; then
      hit[i]=true
      break
    fi
  done
  [[ ${areas[$i]} == all && ${hit[i]} == true ]] && everything=true
done

{
  echo "${#files[@]} changed files that count (Markdown and other ignored files left out)."
  echo
  echo "| Area | Runs |"
  echo "|---|---|"
} >> "$summary"
for i in "${!areas[@]}"; do
  [[ ${areas[$i]} == all || ${areas[$i]} == ignore ]] && continue
  [[ $everything == true ]] && hit[i]=true
  echo "${areas[$i]}=${hit[i]}" >> "$out"
  echo "| ${areas[$i]} | ${hit[i]} |" >> "$summary"
done
