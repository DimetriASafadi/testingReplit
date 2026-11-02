/* Single source for the game's BLUE gem artwork (the same faceted gems shown on
   the luck wheel). Every visual helper must use these instead of local copies. */
const B = () => import.meta.env.BASE_URL;
export const GEM_ICON_URL = () => `${B()}art/wheel/gem.webp`;
export const GEMS_ICON_URL = () => `${B()}art/wheel/gems.webp`;
export const COIN_ICON_URL = () => `${B()}art/wheel/coins.webp`;
export const gemIconHtml = (cls = 'gem-icon', alt = '') => `<img class="${cls}" src="${GEM_ICON_URL()}" alt="${alt}" decoding="async"/>`;
/** Exact coin artwork used by the top HUD wallet (main.ts resourceIcon('coins')). */
export const HUD_COIN_ICON_URL = () => `${B()}art/ui-resources/coins.webp`;
