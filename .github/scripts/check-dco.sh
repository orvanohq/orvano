#!/usr/bin/env bash
# Checks that every commit has a Developer Certificate of Origin sign off.
#
# Reads a JSON array of commits on stdin, in the shape of the GitHub API
# "list commits on a pull request" response (several pages may be concatenated).
# A commit passes when its message has a "Signed-off-by: Name <email>" line whose
# email matches the commit author's email (case insensitive). Merge commits are skipped.
# Exits 1 and lists the failing commits when any commit has no matching sign off.
set -euo pipefail

missing="$(jq -rs '
  add
  | .[]
  | select((.parents | length) < 2)
  | (.commit.author.email | ascii_downcase) as $email
  | [ .commit.message | scan("(?m)^Signed-off-by: [^\n<]*<([^>\n]+)>[ \t]*$") | .[0] | ascii_downcase ] as $signers
  | select(($signers | index($email)) == null)
  | "  \(.sha[0:7])  \(.commit.author.name) <\(.commit.author.email)>  \(.commit.message | split("\n")[0])"
')"

if [[ -n "$missing" ]]; then
  echo "These commits have no Signed-off-by line matching their author email:"
  echo "$missing"
  echo
  echo "Sign off with 'git commit -s'. To fix existing commits, run"
  echo "'git rebase --signoff <base branch>' and push with '--force-with-lease'."
  echo "See CONTRIBUTING.md, section 'Sign off your commits (DCO)'."
  exit 1
fi

echo "Every commit is signed off."
