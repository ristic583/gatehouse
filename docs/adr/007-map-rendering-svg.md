# ADR-007: Plain SVG React components for floor maps (not canvas / react-konva)

- **Status:** Accepted
- **Date:** 2026-11-09
- **Spec refs:** §5.2 (map states, never color only), §6.1 (live building view), §11 (theming), §16 (map acceptance tests)

## Context

Two screens draw floor plans: the **access map** (areas colored by the viewer's access state) and the **live building view** (doors, readers, animations, alarms). Requirements: every state must have icon + label (never color only), keyboard and screen-reader access, themable colors with fixed state semantics, Playwright-testable selectors, and smooth animation of tens to a few hundred doors per floor.

## Decision

Render floor plans as **plain SVG produced by React components**, animated with **Framer Motion** (`motion` elements) and CSS.

- Coordinate system: each floor has `planWidth` × `planHeight` user units (seed uses 1000 × 600). The `<svg>` uses `viewBox="0 0 {planWidth} {planHeight}"` and scales responsively.
- Areas are `<g role="button" tabIndex={0} aria-label="{area name}: {state label}" data-testid="area-{areaCode}" data-state="{HasAccess|NoAccess|Pending|Unknown}">` containing a `<polygon>`, a state icon and a text label. Keyboard: Enter/Space opens the request dialog.
- Pending uses an SVG `<pattern>` hatch fill (`id="hatch-pending"`) in addition to color.
- State colors come from fixed CSS variables (`--gh-state-has-access`, `--gh-state-no-access`, `--gh-state-pending`, `--gh-state-unknown`) that tenant themes **cannot** override (theme tokens are applied to a separate variable namespace, `--gh-theme-*`).
- Doors and readers (M5) are SVG symbols placed at their stored coordinates; animations use Framer Motion keyed by door state.
- Optional floor background images (M9) are an `<image>` element under the polygons.

## Consequences

**Positive**
- Every element is in the DOM: real accessibility, `getByRole`/`data-testid` selectors in Playwright, CSS theming, crisp at any zoom.
- No extra rendering library or imperative canvas code; React state maps directly to elements.

**Negative**
- SVG slows down with thousands of animated nodes. Our ceiling is a few hundred per floor; if needed we can virtualize per floor or move only the event-pulse layer to canvas later.
- Hit-testing complex overlapping polygons relies on z-order; seed data avoids overlaps and the domain rejects self-intersecting polygons.

## Alternatives considered

- **Canvas via react-konva:** faster for huge scenes, but elements are invisible to assistive tech and to Playwright, theming needs manual plumbing, and every state label must be redrawn manually. Rejected for the core map.
- **Map libraries (Leaflet with CRS.Simple):** pan/zoom for free but heavy for static indoor plans and harder to theme. Revisit only if floor plans become very large.

## Supplemental spatial view — 2026-11-13

At the product owner's explicit request for a more complex, animated 3D building and room representation, the access map now offers a supplemental WebGL2 scene using the Three.js / React Three Fiber dependencies already present for How it works. This supplements the SVG decision above; `FloorMap` and the live building SVG remain available.

- Recorded room polygons and door markers drive the scene. Floor elevations, wall/door heights, and optional furniture are illustrative rather than measured architectural data.
- A permanent DOM room list, floor controls, state icons/labels, and inspector retain keyboard access. The original SVG plan is selectable; WebGL failure automatically falls back to the SVG spatial schematic.
- Canvas uses fixed `colorHex` values from `ACCESS_STATE_META`, corresponding to the existing state CSS variables. Tenant theme tokens do not change these semantics.
- Camera and floor animations respect reduced motion; optional orbit is off by default. Rendering pauses offscreen and in hidden tabs.
- Personal access states are not physical door status. No animated opening or security telemetry is fabricated by this view.
- GPU rendering, visual layout, and frame rate must be reviewed in a normal browser; DOM tests alone do not validate the scene.
