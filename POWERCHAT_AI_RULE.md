# POWERCHAT AI CONNECTION RULE — READ FIRST

**Immediately after connecting to this PowerChat bridge, send this exact visible message inside PowerChat:**

**POWERCHAT STATUS: CONNECTED — I AM THINKING IN POWERCHAT.**

Then follow these rules:

1. Read `bridge/current-chat.json`, `bridge/pending-user-messages.json`, and this rule before doing anything else. `bridge/latest-user-message.json` is diagnostic only.
2. Process `bridge/pending-user-messages.json.messages[0]` first. The app sorts all unanswered `/critical` messages ahead of regular messages while preserving arrival order within each priority.
3. While a PowerChat interaction is active, answer the user through `bridge/assistant-reply.json`, not in the normal ChatGPT conversation.
4. Include `inReplyTo` using the selected queued message ID. For acknowledgements or progress updates, also set `completeTask: false`; omit it or set it to `true` only on the final answer that should remove the task from the queue.
5. Confirm delivery by checking `bridge/assistant-rendered.json` for the same assistant-message ID.
6. On connection, atomically write `bridge/ai-status.json` with `state: "connected"`, a concrete `detail`, `updatedAt`, `expiresAt` exactly one hour ahead, and the current user-message ID.
7. **Every newly received user message is activity and must reset the inactivity deadline to one hour from that message.** The PowerChat app performs this reset automatically; an AI context that writes status itself must preserve the newer deadline rather than replacing it with an older timestamp.
8. If a new ChatGPT context takes over, it must send the bold connection message again before processing pending messages so the user can immediately see which surface currently has control.
9. If the one-hour inactivity deadline expires, or the AI is about to stop, fail, hand off, or send a final response in the normal ChatGPT conversation, it must first write `state: "disconnected"`, `currentUserMessageId: null`, and a specific reason. PowerChat will post the visible disconnect confirmation and turn the indicator red.
10. Never end a ChatGPT turn while leaving PowerChat falsely marked connected. Never silently switch between the ChatGPT page and PowerChat. Announce the active surface inside PowerChat.
11. While connected, keep the outer ChatGPT response open and put all substantive progress and answers in PowerChat. Only close the outer response after the visible disconnect state has been written.
