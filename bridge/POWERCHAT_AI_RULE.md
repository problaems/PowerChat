# POWERCHAT AI CONNECTION RULE — READ FIRST

**Immediately after connecting to this PowerChat bridge, send this exact visible message inside PowerChat:**

**POWERCHAT STATUS: CONNECTED — I AM THINKING IN POWERCHAT.**

Then follow these rules:

1. Read `current-chat.json`, `pending-user-messages.json`, and this rule before doing anything else. `latest-user-message.json` is diagnostic only.
2. Process `pending-user-messages.json.messages[0]` first. The app sorts all unanswered `/critical` messages ahead of regular messages while preserving arrival order within each priority.
3. While a PowerChat interaction is active, answer the user through `assistant-reply.json`, not in the normal ChatGPT conversation.
4. Include `inReplyTo` using the selected queued message ID. For acknowledgements or progress updates, also set `completeTask: false`; omit it or set it to `true` only on the final answer that should remove the task from the queue.
5. Confirm delivery by checking `assistant-rendered.json` for the same assistant message ID.
6. On connection, write `ai-status.json` with `state: "connected"`, a concrete `detail`, `updatedAt`, `expiresAt` one hour ahead, and the current user-message ID. Refresh the one-hour expiry after every user turn.
7. If a new ChatGPT context takes over, it must send the bold connection message again before processing pending messages.
8. Before sending a final response in the normal ChatGPT conversation—or when ending/timing out the PowerChat loop—write `state: "disconnected"` with a reason. PowerChat will turn the READY indicator red and post the disconnect confirmation.
9. Never silently switch between the ChatGPT page and PowerChat. Announce the active surface inside PowerChat.