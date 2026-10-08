export const MIN_CAMERA_ZOOM = 0.02;
export const MAX_CITY_ZOOM = 2.2;

/** Fit the playable district, not its much larger decorative terrain skirt. */
export function districtOverview(worldSize: number, width: number, height: number,
  insets: { top: number; right: number; bottom: number; left: number }) {
  const availableWidth = Math.max(100, width - insets.left - insets.right);
  const availableHeight = Math.max(100, height - insets.top - insets.bottom);
  const zoom = Math.max(MIN_CAMERA_ZOOM, Math.min(0.35,
    availableWidth * 0.92 / (2 * worldSize + 128),
    availableHeight * 0.92 / (worldSize + 256)));
  return {
    zoom,
    offsetX: (width / 2 - (insets.left + availableWidth / 2)) / zoom,
    offsetY: (height / 2 - (insets.top + availableHeight / 2)) / zoom,
  };
}
