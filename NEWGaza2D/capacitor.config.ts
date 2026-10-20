import type { CapacitorConfig } from '@capacitor/cli';

const config: CapacitorConfig = {
  appId: 'com.newgaza.citygame',
  appName: 'نيو غزة',
  webDir: 'dist',
  server: {
    androidScheme: 'https',
  },
  android: {
    backgroundColor: '#07182e',
    allowMixedContent: false,
  },
};

export default config;
