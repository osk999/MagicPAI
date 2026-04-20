# Accessibility statement — MagicPAI Studio

Last updated: 2026-04-20.
Status: target for Phase 3 + ongoing.

---

## Commitment

MagicPAI Studio aims to conform to [WCAG 2.2 Level AA](https://www.w3.org/TR/WCAG22/).

We treat accessibility as a first-class requirement, not an afterthought.

---

## Current conformance

**Status:** Not yet audited (Phase 3 target).

Pre-migration state (Elsa Studio-based UI): partial conformance via MudBlazor's
accessible components. Several custom pages had known issues.

Post-migration (Phase 3+): full WCAG 2.2 AA audit planned.

---

## What's supported

Per `temporal.md` §WW:

- Keyboard navigation: every interactive element reachable via Tab.
- Focus visible: MudBlazor default focus outlines preserved.
- Labels: every form input has an associated label.
- ARIA: icons have `aria-label`; live regions for dynamic content.
- Contrast: MudBlazor default theme meets AA (verified with `pa11y`).
- Screen readers: tested with NVDA (Windows).
- Reduced motion: `prefers-reduced-motion` respected.

## Known issues

*(to be filled post-audit)*

## Keyboard shortcuts

| Shortcut | Action |
|---|---|
| `Tab` / `Shift+Tab` | Navigate focusable elements |
| `Enter` (in form) | Submit |
| `Ctrl+Enter` | Submit session (SessionInputForm) |
| `Esc` | Close dialog |

(More shortcuts post-migration as UI evolves.)

## Assistive technology compatibility

Tested with:
- **NVDA** (Windows) — primary.
- **JAWS** (Windows) — occasional.
- **VoiceOver** (macOS) — occasional.

Not yet tested with:
- Mobile screen readers (TalkBack, VoiceOver iOS). Studio is desktop-first.

## Browsers

Chrome/Edge 120+, Firefox 115+, Safari 16+ officially supported.

Older browsers may have degraded accessibility; not a blocker for the primary
(internal, enterprise) audience.

## Automated testing

CI runs:
- `pa11y` scan of key pages (session create, session list, session detail).
- `axe-core` via Playwright on smoke test pages.

## Manual testing

Quarterly:
- Keyboard-only navigation across all flows.
- NVDA reading across key pages.
- Contrast check with [WebAIM Contrast Checker](https://webaim.org/resources/contrastchecker/).

## Reporting issues

- Internal: open a ticket in Jira with `accessibility` label.
- External (if app ever becomes public): `accessibility@example.com`.

We aim to respond within 3 business days.

## Resources

- [WCAG 2.2](https://www.w3.org/TR/WCAG22/)
- [MudBlazor accessibility](https://mudblazor.com/getting-started/installation)
- `temporal.md` §WW — accessibility engineering details.

## History

- 2026-04-20 — Initial statement. Audit not yet completed.
- (Post-Phase-3 audit results will be added here.)
