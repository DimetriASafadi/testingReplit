package com.newgaza.citygame;

import android.os.Handler;
import android.os.Looper;
import androidx.annotation.NonNull;
import com.getcapacitor.JSObject;
import com.getcapacitor.Plugin;
import com.getcapacitor.PluginCall;
import com.getcapacitor.PluginMethod;
import com.getcapacitor.annotation.CapacitorPlugin;
import com.google.android.gms.ads.AdError;
import com.google.android.gms.ads.AdRequest;
import com.google.android.gms.ads.FullScreenContentCallback;
import com.google.android.gms.ads.LoadAdError;
import com.google.android.gms.ads.MobileAds;
import com.google.android.gms.ads.RequestConfiguration;
import com.google.android.gms.ads.rewarded.RewardedAd;
import com.google.android.gms.ads.rewarded.RewardedAdLoadCallback;
import com.google.android.ump.ConsentInformation;
import com.google.android.ump.ConsentRequestParameters;
import com.google.android.ump.UserMessagingPlatform;
import java.util.ArrayList;
import java.util.List;

/** Local SDK grant bridge. No server-side verification is claimed by this plugin. */
@CapacitorPlugin(name = "NewGazaAds")
public class NewGazaAdsPlugin extends Plugin {
    private final Handler handler = new Handler(Looper.getMainLooper());
    private final List<PluginCall> consentCalls = new ArrayList<>();
    private ConsentInformation consent;
    private boolean initialized = false, gathering = false, showing = false;
    private RewardedAd prepared;
    private PluginCall loadingCall, showingCall;
    private String requestId;
    private long loadedAt;
    private boolean earned;
    private Runnable loadTimeout;

    private boolean canRequest() { return BuildConfig.ADS_TEST_MODE || (consent != null && consent.canRequestAds()); }
    private JSObject consentResult() {
        JSObject r = new JSObject();
        r.put("canRequest", canRequest());
        r.put("privacyRequired", consent != null && consent.getPrivacyOptionsRequirementStatus()
            == ConsentInformation.PrivacyOptionsRequirementStatus.REQUIRED);
        return r;
    }
    @PluginMethod public void initialize(PluginCall call) {
        getActivity().runOnUiThread(() -> {
            if (initialized) { call.resolve(consentResult()); return; }
            consentCalls.add(call);
            if (gathering) return;
            gathering = true;
            consent = UserMessagingPlatform.getConsentInformation(getContext());
            ConsentRequestParameters params = new ConsentRequestParameters.Builder()
                .setTagForUnderAgeOfConsent(BuildConfig.ADS_UNDER_AGE).build();
            consent.requestConsentInfoUpdate(getActivity(), params,
                () -> UserMessagingPlatform.loadAndShowConsentFormIfRequired(getActivity(), error -> consentFinished()),
                error -> consentFinished());
        });
    }
    private void consentFinished() {
        if (!canRequest()) {
            gathering = false;
            for (PluginCall c : consentCalls) c.reject("CONSENT");
            consentCalls.clear(); return;
        }
        MobileAds.setRequestConfiguration(new RequestConfiguration.Builder()
            .setTagForChildDirectedTreatment(BuildConfig.ADS_CHILD_DIRECTED ? RequestConfiguration.TAG_FOR_CHILD_DIRECTED_TREATMENT_TRUE : RequestConfiguration.TAG_FOR_CHILD_DIRECTED_TREATMENT_FALSE)
            .setTagForUnderAgeOfConsent(BuildConfig.ADS_UNDER_AGE ? RequestConfiguration.TAG_FOR_UNDER_AGE_OF_CONSENT_TRUE : RequestConfiguration.TAG_FOR_UNDER_AGE_OF_CONSENT_FALSE)
            .build());
        // Only the official test inventory can proceed if UMP has no message on Google's sample app.
        MobileAds.initialize(getContext(), status -> getActivity().runOnUiThread(() -> {
            initialized = true; gathering = false;
            for (PluginCall c : consentCalls) c.resolve(consentResult());
            consentCalls.clear();
        }));
    }
    @PluginMethod public void privacy(PluginCall call) {
        getActivity().runOnUiThread(() -> {
            if (showing || loadingCall != null || gathering) { call.reject("BUSY"); return; }
            if (consent == null) consent = UserMessagingPlatform.getConsentInformation(getContext());
            if (consent.getPrivacyOptionsRequirementStatus() != ConsentInformation.PrivacyOptionsRequirementStatus.REQUIRED) {
                call.reject(BuildConfig.ADS_TEST_MODE ? "PRIVACY_TEST" : "PRIVACY_UNAVAILABLE"); return;
            }
            prepared = null;
            UserMessagingPlatform.showPrivacyOptionsForm(getActivity(), error -> {
                if (error != null) call.reject("CONSENT");
                else call.resolve();
            });
        });
    }
    @PluginMethod public void prepare(PluginCall call) {
        getActivity().runOnUiThread(() -> {
            String id = call.getString("requestId"), unit = call.getString("unitId");
            if (showing || loadingCall != null || !initialized) { call.reject("BUSY"); return; }
            if (!canRequest()) { call.reject("CONSENT"); return; }
            if (id == null || !id.matches("ad-[1-9]\\d*") || unit == null || !unit.matches("ca-app-pub-\\d+/\\d+")
                || (BuildConfig.ADS_TEST_MODE && !unit.equals("ca-app-pub-3940256099942544/5224354917"))) {
                call.reject("CONFIG"); return;
            }
            prepared = null; requestId = id; loadingCall = call;
            loadTimeout = () -> {
                if (loadingCall == call) { loadingCall = null; requestId = null; call.reject("TIMEOUT"); }
            };
            handler.postDelayed(loadTimeout, 45000);
            RewardedAd.load(getContext(), unit, new AdRequest.Builder().build(), new RewardedAdLoadCallback() {
                @Override public void onAdFailedToLoad(@NonNull LoadAdError error) {
                    if (loadingCall != call) return;
                    handler.removeCallbacks(loadTimeout); loadingCall = null; requestId = null;
                    call.reject(error.getCode() == 3 ? "NO_FILL" : "LOAD_FAILED");
                }
                @Override public void onAdLoaded(@NonNull RewardedAd ad) {
                    if (loadingCall != call) return;
                    handler.removeCallbacks(loadTimeout); loadingCall = null;
                    prepared = ad; loadedAt = System.currentTimeMillis(); call.resolve();
                }
            });
        });
    }
    @PluginMethod public void show(PluginCall call) {
        getActivity().runOnUiThread(() -> {
            if (showing || prepared == null || requestId == null || !requestId.equals(call.getString("requestId"))
                || System.currentTimeMillis() - loadedAt > 55 * 60000L) { call.reject("NOT_READY"); return; }
            if (!canRequest()) { prepared = null; call.reject("CONSENT"); return; }
            final String activeId = requestId;
            RewardedAd ad = prepared; prepared = null; showing = true; showingCall = call; earned = false;
            ad.setFullScreenContentCallback(new FullScreenContentCallback() {
                @Override public void onAdFailedToShowFullScreenContent(@NonNull AdError error) {
                    showing = false; showingCall = null; requestId = null; call.reject("SHOW_FAILED");
                }
                @Override public void onAdDismissedFullScreenContent() {
                    // Grace for callback reordering; only onUserEarnedReward sets earned.
                    handler.postDelayed(() -> {
                        showing = false; showingCall = null; requestId = null;
                        JSObject result = new JSObject(); result.put("earned", earned); call.resolve(result);
                    }, 750);
                }
            });
            ad.show(getActivity(), reward -> {
                if (!earned) {
                    earned = true;
                    JSObject r = new JSObject(); r.put("requestId", activeId);
                    notifyListeners("rewardEarned", r);
                }
            });
        });
    }
    @PluginMethod public void discard(PluginCall call) {
        getActivity().runOnUiThread(() -> {
            if (!showing) {
                prepared = null; requestId = null;
                if (loadingCall != null) { handler.removeCallbacks(loadTimeout); loadingCall.reject("CANCELLED"); loadingCall = null; }
            }
            call.resolve();
        });
    }
    @Override protected void handleOnDestroy() {
        handler.removeCallbacksAndMessages(null); prepared = null;
        if (loadingCall != null) loadingCall.reject("INTERRUPTED");
        if (showingCall != null) showingCall.reject("INTERRUPTED");
        for (PluginCall c : consentCalls) c.reject("INTERRUPTED");
        consentCalls.clear(); super.handleOnDestroy();
    }
}
