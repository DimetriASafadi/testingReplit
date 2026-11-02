import type Phaser from 'phaser';
export const FARM_CANOPIES=['olive','citrus','orn'] as const;
export function preloadFarmCanopies(scene:Phaser.Scene){
  for(const kind of FARM_CANOPIES)scene.load.image(`canopy:${kind}`,
    `${import.meta.env.BASE_URL}art/realistic/canopies/${kind}.webp?v=canonical-farms-20261102`);
}
