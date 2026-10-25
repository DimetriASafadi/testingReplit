package com.newgaza.citygame;

import com.getcapacitor.BridgeActivity;
import android.os.Bundle;

public class MainActivity extends BridgeActivity {
    @Override public void onCreate(Bundle savedInstanceState) {
        registerPlugin(NewGazaAdsPlugin.class);
        super.onCreate(savedInstanceState);
    }
}
