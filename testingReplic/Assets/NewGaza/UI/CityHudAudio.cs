using UnityEngine;
using NewGaza.Core;

namespace NewGaza
{
    public sealed partial class CityHud
    {
        private void BuildAudioSettings()
        {
            CityAudio audio = session.Audio;
            if (audio == null) return;
            var card = ListCard("الصوت وأجواء المدينة", 424);
            CardLabel(card, "عند التقريب يبرز صوت المعدّة القريبة؛ عند الابتعاد تسمع هواءً خفيفًا.\nتُحفظ خيارات الصوت دون تغيير تقدّم اللعبة.",
                57, 76, 18, Muted);
            var mute = CardButton(card, audio.IsMuted ? "تشغيل الصوت" : "كتم جميع الأصوات",
                0, 138, null, true, Teal, 1);
            mute.onClick.AddListener(() =>
            {
                audio.PlayClick();
                audio.ToggleMute();
                ButtonText(mute, audio.IsMuted ? "تشغيل الصوت" : "كتم جميع الأصوات");
            });
            AudioVolumeRow(card, audio, CityAudioChannel.Master, "المستوى العام", 209);
            AudioVolumeRow(card, audio, CityAudioChannel.Equipment, "المعدات", 260);
            AudioVolumeRow(card, audio, CityAudioChannel.Ambience, "الهواء والبحر", 311);
            AudioVolumeRow(card, audio, CityAudioChannel.Interface, "الأزرار", 362);
        }

        private void AudioVolumeRow(RectTransform card, CityAudio audio, CityAudioChannel channel,
            string title, float top)
        {
            var label = CardLabel(card, "", top, 44, 18, Cream);
            label.rectTransform.offsetMin = new Vector2(178, -top - 44);
            void RefreshVolume() { label.SetText(title + " · " + Mathf.RoundToInt(audio.Volume(channel) * 100f) + "%"); }
            var down = ActionButton(card, "−", () =>
            {
                audio.SetVolume(channel, audio.Volume(channel) - .1f);
                RefreshVolume();
            }, Card);
            var up = ActionButton(card, "+", () =>
            {
                audio.SetVolume(channel, audio.Volume(channel) + .1f);
                RefreshVolume();
            }, Card);
            Place(down.GetComponent<RectTransform>(), 16, top, 70, 42, true);
            Place(up.GetComponent<RectTransform>(), 94, top, 70, 42, true);
            RefreshVolume();
        }
    }
}