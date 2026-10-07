export interface CanvasPointer {
  id: number; identifier: number; wasTouch: boolean; isDown: boolean;
}

/** Only contacts that began on the canvas may participate in camera gestures. */
export class CanvasContacts<T extends CanvasPointer> {
  private contacts = new Map<number, { pointer: T; identifier: number }>();
  start(pointer: T) { this.contacts.set(pointer.id, { pointer, identifier: pointer.identifier }); }
  has(pointer: T) { const e = this.contacts.get(pointer.id); return e?.pointer === pointer && e.identifier === pointer.identifier; }
  end(pointer: T) { this.contacts.delete(pointer.id); }
  clear() { this.contacts.clear(); }
  live(touches?: ReadonlySet<number>): T[] {
    for (const [id, entry] of this.contacts) {
      const p = entry.pointer;
      if (!p.isDown || p.identifier !== entry.identifier ||
          (p.wasTouch && touches && !touches.has(entry.identifier))) this.contacts.delete(id);
    }
    return Array.from(this.contacts.values(), e => e.pointer);
  }
}

export function nativeTouchIds(event: Event): Set<number> | undefined {
  if (!('touches' in event)) return undefined;
  return new Set(Array.from((event as TouchEvent).touches, t => t.identifier));
}

/** A removed UI button may never bubble touchend. Free its slot on the next real touchstart. */
export function releaseStaleTouches<T extends CanvasPointer & { reset(): void }>(
  pointers: readonly T[], live: ReadonlySet<number>, starting: ReadonlySet<number> = new Set(),
) {
  // Native identifiers may be recycled. A newly starting ID cannot belong to
  // an already-held finger, even when the stale engine slot has that same ID.
  for (const p of pointers) if (p.wasTouch && p.isDown &&
    (!live.has(p.identifier) || starting.has(p.identifier))) p.reset();
}
