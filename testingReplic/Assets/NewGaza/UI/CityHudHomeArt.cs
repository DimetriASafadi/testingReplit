using System;
using UnityEngine;
using UnityEngine.UI;

namespace NewGaza
{
    public sealed partial class CityHud
    {
        private Texture2D homeAtlas;
        private readonly Sprite[] homeSprites = new Sprite[10];
        private Camera homePreviewCamera;
        private RenderTexture homePreviewTexture;
        private RawImage homePreviewImage;
        private int homePreviewDistrict = -1;
        private float homeNextPreview;

        private void HomeArt(Image image, int slot)
        {
            if (slot < 0 || slot >= homeSprites.Length) throw new ArgumentOutOfRangeException(nameof(slot));
            if (homeAtlas == null) homeAtlas = Resources.Load<Texture2D>("Menu/MenuIcons");
            if (homeAtlas == null) throw new InvalidOperationException("Missing Resources/Menu/MenuIcons.png");
            if (homeSprites[slot] == null)
            {
                float w = homeAtlas.width / 5f, h = homeAtlas.height / 2f;
                homeSprites[slot] = Sprite.Create(homeAtlas,
                    new Rect((slot % 5) * w, (1 - slot / 5) * h, w, h), new Vector2(.5f, .5f), 100);
                homeSprites[slot].name = "New Gaza home icon " + slot;
            }
            image.sprite = homeSprites[slot];
            image.preserveAspect = true;
            image.color = Color.white;
            image.raycastTarget = false;
        }

        private void HomeDistrictPreview(RawImage image)
        {
            homePreviewImage = image;
            image.raycastTarget = false;
            homePreviewTexture = new RenderTexture(384, 216, 16) { name = "Live district thumbnail" };
            homePreviewTexture.Create();
            image.texture = homePreviewTexture;
            homePreviewCamera = new GameObject("District thumbnail camera").AddComponent<Camera>();
            homePreviewCamera.transform.SetParent(transform, false);
            homePreviewCamera.CopyFrom(Camera.main);
            homePreviewCamera.enabled = false;
            homePreviewCamera.targetTexture = homePreviewTexture;
        }

        private void RefreshHomePreview(int district)
        {
            if (!MainMenuVisible || homePreviewCamera == null ||
                (district == homePreviewDistrict && Time.unscaledTime < homeNextPreview)) return;
            homePreviewDistrict = district; homeNextPreview = Time.unscaledTime + 6f;
            var rotation = Camera.main.transform.rotation;
            var centre = world.DistrictPosition(district);
            homePreviewCamera.transform.SetPositionAndRotation(centre - rotation * Vector3.forward * 180f, rotation);
            homePreviewCamera.orthographic = true;
            homePreviewCamera.orthographicSize = world.DistrictViewingSize(district) * .65f;
            homePreviewCamera.aspect = 384f / 216f;
            homePreviewCamera.Render();
        }

        private void DisposeHomeArt()
        {
            foreach (var sprite in homeSprites) if (sprite != null) Destroy(sprite);
            if (homePreviewCamera != null) Destroy(homePreviewCamera.gameObject);
            if (homePreviewTexture != null) { homePreviewTexture.Release(); Destroy(homePreviewTexture); }
        }
    }
}