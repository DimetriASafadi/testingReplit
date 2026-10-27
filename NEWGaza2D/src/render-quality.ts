export type DeviceTier = 'low' | 'balanced' | 'high';
export interface RenderProfile {
  tier: DeviceTier;
  pixelDensity: number;
  maxPixels: number;
  groundTileSize: number;
  plotBatchSize: number;
  parallelDownloads: number;
}

export function renderDensity(dpr: number, width: number, height: number,
  maxDpr = 2, maxPixels = 2_500_000) {
  return Math.max(1, Math.min(maxDpr, dpr || 1,
    Math.sqrt(maxPixels / Math.max(1, width * height))));
}

/** Keep original sprite artwork intact; scale only the framebuffer and work rate. */
export function chooseRenderProfile(input: {
  memoryGb?: number;
  cores?: number;
  dpr: number;
  width: number;
  height: number;
}): RenderProfile {
  const lowMemory = input.memoryGb != null && input.memoryGb > 0 && input.memoryGb <= 4;
  const lowCpu = input.cores != null && input.cores > 0 && input.cores <= 4;
  const highEnd = input.memoryGb != null && input.memoryGb >= 8 &&
    input.cores != null && input.cores >= 8;
  const tier: DeviceTier = lowMemory || lowCpu ? 'low' : highEnd ? 'high' : 'balanced';
  const limits = tier === 'low'
    ? { maxDpr: 1.25, maxPixels: 1_500_000, groundTileSize: 1024, plotBatchSize: 1, parallelDownloads: 2 }
    : tier === 'high'
      ? { maxDpr: 2, maxPixels: 2_500_000, groundTileSize: 2048, plotBatchSize: 4, parallelDownloads: 4 }
      : { maxDpr: 1.5, maxPixels: 2_000_000, groundTileSize: 1536, plotBatchSize: 2, parallelDownloads: 3 };
  return Object.freeze({
    tier,
    pixelDensity: renderDensity(input.dpr, input.width, input.height, limits.maxDpr, limits.maxPixels),
    maxPixels: limits.maxPixels,
    groundTileSize: limits.groundTileSize,
    plotBatchSize: limits.plotBatchSize,
    parallelDownloads: limits.parallelDownloads,
  });
}

function browserProfile(): RenderProfile {
  if (typeof window === 'undefined') return chooseRenderProfile({ dpr: 1, width: 1, height: 1 });
  const nav = navigator as Navigator & { deviceMemory?: number };
  return chooseRenderProfile({
    memoryGb: nav.deviceMemory,
    cores: nav.hardwareConcurrency,
    dpr: window.devicePixelRatio,
    width: window.innerWidth,
    height: window.innerHeight,
  });
}

export const DEVICE_RENDER_PROFILE = browserProfile();
export const PIXEL_DENSITY = DEVICE_RENDER_PROFILE.pixelDensity;
