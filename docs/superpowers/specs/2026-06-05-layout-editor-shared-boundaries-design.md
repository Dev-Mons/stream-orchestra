# Layout Editor Shared Boundary Design

## Summary

The layout editor should keep the current free-form slot layout capability, but change resizing from independent slot-edge dragging to boundary-aware editing. In the default mode, users resize shared boundary groups so adjacent slots stay connected without holes. When Ctrl is held, the editor exposes finer individual shared-edge handles, but only when two slots share the exact same edge length. Both modes apply snapping to nearby parallel boundaries that overlap the dragged segment.

## Goals

- Prevent accidental holes and overlaps during normal layout editing.
- Preserve the ability to create detailed non-uniform layouts.
- Make it visually clear which boundaries are adjustable in the current mode.
- Allow users to re-align detailed edits by snapping boundaries back to nearby compatible lines.
- Keep saved layouts compatible with the existing `LayoutPreset` explicit bounds model.

## Non-Goals

- Replacing explicit slot bounds with a pure grid-only layout model.
- Introducing a full constraint solver.
- Supporting non-rectangular slots.
- Changing playback, workspace restore, or WebView profile behavior.

## Current Context

The editor stores custom slots as explicit normalized bounds through `LayoutSlot.Left`, `Top`, `Width`, and `Height`. `LayoutSlotBoundsCalculator` already reads either explicit bounds or grid-derived bounds. `LayoutEditorDialog` currently resizes one slot edge at a time through per-slot resize thumbs. `LayoutEditorGridGeometry` already contains logic for deriving vertical and horizontal splitter segments from grid layouts, which is a useful pattern for the new boundary geometry model.

## Concepts

### Boundary Segment

A boundary segment is a normalized line extracted from slot bounds.

- Direction: vertical or horizontal.
- Coordinate: `x` for vertical, `y` for horizontal.
- Span: the segment range on the opposite axis.
- Adjacent slots: the slot on each side of the boundary.

For a vertical segment, the adjacent slots are left and right. For a horizontal segment, they are top and bottom.

### Exact Shared Edge

Two slots have an exact shared edge only when:

- Their touching boundary coordinates are equal within the editor geometry tolerance.
- Their spans on the opposite axis are equal within tolerance.
- One slot is on each side of the boundary.
- The edge is not an outer layout boundary.

This is the central rule for Ctrl-mode editing:

> Individual edge adjustment is possible only when two slots share the exact same edge length.

### Boundary Group

A boundary group is a set of shared boundary segments that represent one aligned adjustable boundary in default mode.

Segments belong to the same group when:

- They have the same direction.
- They have the same boundary coordinate within tolerance.
- Their spans form one continuous chain after sorting, where each neighboring span touches or overlaps within tolerance.

Dragging a group moves all member segments together. This means a common column or row split remains aligned across the layout.

## Interaction Design

### Default Mode

Default mode shows group handles.

- A handle appears at the visual center of each adjustable boundary group.
- Horizontal boundaries use a circular handle with `=`.
- Vertical boundaries use a circular handle with the rotated equivalent, displayed as `||`.
- Dragging a handle moves every segment in that boundary group.
- Adjacent slots are resized together so no hole appears between them.
- The move is rejected if it would make any affected slot smaller than the minimum size or create overlap.

Example:

```text
1 | 2 | 3
---------
4 | 5 | 6
```

Dragging the boundary between `1` and `2` in default mode also moves the aligned boundary between `4` and `5`.

### Ctrl Mode

Ctrl mode shows individual exact-shared-edge handles.

- A handle appears only between two slots that share the exact same edge length.
- Dragging affects only those two slots.
- The same no-hole, no-overlap, minimum-size validation still applies.
- If moving a candidate edge would break rectangular slot geometry or invalidate neighboring relationships, the handle is not shown or the drag is rejected.

Example:

```text
1 | 2 | 3
---------
4 | 5 | 6
```

Holding Ctrl and dragging the boundary between `1` and `2` moves only that pair's shared edge, leaving `4` and `5` unchanged, as long as the resulting layout stays valid.

## Snapping

Snapping applies in both default mode and Ctrl mode.

### Snap Candidates

The editor only considers candidates that:

- Have the same direction as the dragged boundary.
- Are not part of the active drag set.
- Have a span that overlaps the currently dragged segment span.
- Would still pass minimum-size, no-overlap, and no-hole validation after snapping.

For vertical drags, candidates are other vertical boundaries with overlapping `y` spans. For horizontal drags, candidates are other horizontal boundaries with overlapping `x` spans.

This avoids surprising long-distance snaps to unrelated boundaries.

### Snap Behavior

During drag:

- Compute the intended new coordinate from pointer movement.
- Find the closest valid snap candidate within the snap threshold.
- If one exists, use the candidate coordinate instead of the raw pointer coordinate.
- If no candidate is valid, use the raw coordinate.

Snapping should be visible through the handle and previewed slot geometry, but it should not require a separate mode.

## Geometry Validation

Every drag result must pass validation before it is committed to the editor state.

Required checks:

- All slots remain within `[0, 1]` normalized bounds.
- Every slot width and height stays above `LayoutSlotBoundsCalculator.MinRelativeSize`.
- No two slots overlap.
- The modified shared boundary remains closed by adjacent slots, so no hole is introduced.
- All slots remain rectangular.

The validation should be implemented as a pure geometry service so it can be unit tested without WPF UI.

## Proposed Architecture

### LayoutEditorBoundaryGeometry

Create a new geometry helper for explicit-bound slot editing.

Responsibilities:

- Extract boundary segments from editor slot bounds.
- Detect exact shared edges.
- Build default-mode boundary groups.
- Build Ctrl-mode individual handles.
- Find snap candidates using overlapping spans.
- Apply a proposed drag to the affected slots.
- Validate the resulting slot set.

This keeps geometric rules out of `LayoutEditorDialog`.

### LayoutEditorDialog

Update the dialog to consume geometry results instead of creating per-slot resize handles directly.

Responsibilities:

- Track whether Ctrl is currently held.
- Render default or Ctrl-mode boundary handles.
- Convert drag deltas from pixels to normalized coordinates.
- Ask the geometry helper for the next valid slot set during drag.
- Update editor chrome and surface positions after accepted moves.

### Tests

Add focused tests around the geometry helper before broad UI layout tests.

Required test coverage:

- Extracts shared vertical and horizontal boundaries from explicit slot bounds.
- Groups aligned boundaries in default mode.
- Exposes Ctrl handles only for exact same-length shared edges.
- Rejects Ctrl handles when spans only partially overlap.
- Moves default groups without creating holes.
- Moves Ctrl individual edges without moving unrelated aligned edges.
- Snaps only to same-direction overlapping-span boundaries.
- Does not snap to parallel boundaries with no span overlap.
- Rejects snap results that would violate minimum size or overlap rules.

## Acceptance Criteria

- In a `1 | 2 | 3` layout, moving the `1|2` boundary resizes `1` and `2` without leaving a gap.
- In a two-row `1|2|3 / 4|5|6` layout, default-mode movement of `1|2` also moves `4|5`.
- In the same layout, Ctrl-mode movement of `1|2` moves only `1` and `2` when their shared edge is exact.
- Ctrl-mode handles do not appear for edges that do not share the exact same edge length.
- Dragged boundaries snap only to nearby parallel boundaries with overlapping spans.
- Snapping works in both default and Ctrl modes.
- Saved custom layouts remain valid `LayoutPreset` objects with explicit normalized bounds.

## Implementation Notes

- Use a small tolerance for floating-point comparisons so persisted normalized values do not fail equality checks due to rounding.
- Use an initial snap threshold of 8 screen pixels, converted to normalized distance from the current editor surface size.
- Prefer immutable editor slot updates so drag attempts can be validated before replacing `_editorSlots`.
- Existing dirty or custom layouts should continue to load through the same explicit-bounds path.
