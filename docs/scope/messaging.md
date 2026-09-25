# Epic: Messaging & Notification Center

Outbound messaging, then Orvano's first differentiator: a full notification center. Appwrite and Supabase stop at "send a push or an email"; Orvano gives you the inbox, preferences, and fan out. See [index.md](index.md) for the full plan.

### 24. Messaging providers & push · needs a decision
Connect email, SMS, and push providers per project; Flutter and web SDKs register device tokens; send to users, topics, or devices from server SDKs or the console.
**Done when:** a message sent from the console or the .NET SDK arrives as a push on a Flutter phone, as a web push in Next.js, and as an SMS, with delivery status shown.
- [ ] Design it (spec): `/architect messaging providers & push`

### 25. In app inbox ★ · needs a decision
A notifications feed per user: unread counts, read, archive, and actions, delivered live over realtime. Prebuilt inbox widgets for Flutter and React/Next.js so you drop in a bell icon and a panel.
**Done when:** a notification sent from a server SDK appears instantly in the Flutter and Next.js inbox widgets, unread counts stay correct across devices, and read state syncs.
- [ ] Design it (spec): `/architect in app inbox`

### 26. Notification workflows & preferences ★ · needs a decision
One call triggers a workflow that fans out to inbox, push, email, and SMS by each user's channel preferences, with templates, delays, and digests (batch many events into one message).
**Done when:** a triggered workflow reaches each user only on channels they allow, a digest groups events over a window, and users manage preferences from a prebuilt widget.
- [ ] Design it (spec): `/architect notification workflows & preferences`
