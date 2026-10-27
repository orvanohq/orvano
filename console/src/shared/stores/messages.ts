import { createStore, useStore } from "./create-store";

// Spec 0001 UX-24 message history (WCAG 2.2.1): the session's last 50 messages, newest first, so a toast that closed
// can still be read. Only `notify` (spec 0015 PRM-8) adds to it; the activity indicator reads it. In memory only.

export type MessageTone = "success" | "error" | "info";

export interface Message {
  id: string;
  tone: MessageTone;
  message: string;
  /** Where the message came from ("Deployments", "Copy"), shown beside it in the history. */
  source: string;
  at: Date;
}

export const MESSAGE_HISTORY_LIMIT = 50;

const messages = createStore<readonly Message[]>([]);
let nextId = 0;

export function addMessage(entry: Omit<Message, "id" | "at">): Message {
  const message: Message = { ...entry, id: `message-${String(++nextId)}`, at: new Date() };
  messages.set((list) => [message, ...list].slice(0, MESSAGE_HISTORY_LIMIT));
  return message;
}

export function getMessages(): readonly Message[] {
  return messages.get();
}

export function useMessages(): readonly Message[] {
  return useStore(messages, (list) => list);
}

/** Test only: forgets the history between tests. */
export function resetMessagesForTests(): void {
  messages.set([]);
}
