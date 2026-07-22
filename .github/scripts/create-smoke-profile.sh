#!/usr/bin/env bash
set -euo pipefail

root="${1:?Usage: create-smoke-profile.sh <root>}"
profile="$root/profile"
account="$profile/ImapMail/imap.example.com"
mkdir -p "$account"

cat > "$profile/prefs.js" <<'EOF'
user_pref("mail.account.account1.server", "server1");
user_pref("mail.server.server1.type", "imap");
user_pref("mail.server.server1.hostname", "imap.example.com");
user_pref("mail.server.server1.userName", "user@example.com");
user_pref("mail.server.server1.directory-rel", "[ProfD]ImapMail/imap.example.com");
EOF

cat > "$account/msgFilterRules.dat" <<'EOF'
version="9"
logging="no"
name="Synthetic smoke rule"
enabled="yes"
type="1"
action="Mark read"
condition="AND (from,contains,tester@example.com)"
EOF
