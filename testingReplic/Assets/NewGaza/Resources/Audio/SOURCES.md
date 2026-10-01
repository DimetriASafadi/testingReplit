# New Gaza audio

These original effects were generated through Replit's ElevenLabs sound-effect
service for this game. They are not recordings of actual machinery in Gaza, nor
audio extracted from Civilization or another game.

| Resource | Purpose |
| --- | --- |
| excavator_engine | Compact excavator diesel idle/load |
| truck_engine | Small construction truck diesel loop |
| dozer_engine | Tracked dozer engine loop |
| hydraulics | Hydraulic pump/piston movement |
| tracks | Steel tracks rolling on gravel |
| wind_high | Calm elevated-view/coastal dry-air ambience |
| coastal_surf | Quiet Mediterranean shore waves |
| ui_click | Restrained tactile button click |
| ui_confirm | Short, gentle action-result cue |

Runtime resources are 24 kHz mono PCM16 WAV. The Unity importer applies mobile
Vorbis settings. Seven loops have an equal-power seam crossfade; their source
hashes, cooked hashes, durations, levels and sample format are recorded in
`AudioManifest.json`. `tools/prepare_equipment_audio.py` reproduces processing from
the original MP3 files in `attached_assets/generated_audio/`.

No network service is used while playing. These are short effects, not music,
voices, alarms or sounds of combat. Spatial focus, filtering and all volume
preferences are presentation-only and do not change economic saved progress.