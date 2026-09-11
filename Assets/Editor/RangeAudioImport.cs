using UnityEditor;
using UnityEngine;

namespace IntuitiveDesigns.ShootingRange.EditorTools
{
    public class RangeAudioImport : AssetPostprocessor
    {
        public const string Root = "Assets/Sounds/ShootingRange/";

        private void OnPreprocessAudio()
        {
            if (!assetPath.StartsWith(Root)) return;

            var importer = (AudioImporter)assetImporter;

            // World sounds are positioned in 3D, so a stereo image is thrown away and costs double
            importer.forceToMono = assetPath.StartsWith(Root + "World/");
            importer.preloadAudioData = true;
            importer.loadInBackground = false;

            var settings = importer.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.DecompressOnLoad;
            settings.compressionFormat = AudioCompressionFormat.ADPCM;
            settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            importer.defaultSampleSettings = settings;

            importer.ClearSampleSettingOverride("Android");
        }
    }
}
