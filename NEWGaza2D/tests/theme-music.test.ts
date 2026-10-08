import { test } from 'node:test';
import { strict as assert } from 'node:assert';
import { ThemeMusic } from '../src/theme-music';

test('the supplied theme holds 40% in loading/menu and fades to 15% in the city without restarting', () => {
  const calls: number[] = [];
  let plays = 0, pauses = 0;
  let paused = true;
  const media = {
    get paused() { return paused; }, currentTime: 28, volume: 1, loop: false, preload: '',
    addEventListener() {}, load() {},
    play() { paused = false; plays++; return Promise.resolve(); },
    pause() { paused = true; pauses++; },
  } as unknown as HTMLAudioElement;
  const param = {
    value: 0,
    cancelScheduledValues() {},
    setValueAtTime(value: number) { this.value = value; },
    linearRampToValueAtTime(value: number) { this.value = value; calls.push(value); },
  };
  const node = () => ({ connect() {} });
  const context = {
    currentTime: 10, destination: {},
    createMediaElementSource: () => node(),
    createGain: () => ({ ...node(), gain: param }),
  } as unknown as AudioContext;

  const music = new ThemeMusic('/audio/victory-theme.mp3', media);
  assert.equal(media.volume, .4);
  assert.equal(music.targetLevel, .4);
  music.start(context);
  assert.equal(media.loop, true);
  assert.equal(plays, 1);
  assert.equal(calls.at(-1), .4);
  music.setPhase('menu');
  assert.equal(calls.length, 1);
  music.setPhase('play');
  assert.equal(calls.at(-1), .15);
  music.setPhase('play');
  assert.equal(calls.length, 2, 'a UI refresh must not reset the transition');
  music.setPhase('loading');
  assert.equal(calls.at(-1), .4);
  music.setMaster(.35);
  assert.equal(calls.at(-1), .2);
  music.setEnabled(false);
  assert.equal(calls.at(-1), 0);
  assert.equal(pauses, 1);
  music.setEnabled(true);
  assert.equal(plays, 2);
  assert.equal(media.currentTime, 28, 'resuming should keep the playback position');
});
