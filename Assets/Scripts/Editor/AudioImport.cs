using UnityEditor;
using UnityEngine;

namespace BIS.EditorTools
{
    /// <summary>
    /// Import settings for Resources/Audio: sound effects decompress on load (short, played often, and Sfx reads
    /// their samples to match loudness); music streams from compressed Vorbis so it costs a few MB in the build.
    /// </summary>
    public class AudioImport : AssetPostprocessor
    {
        void OnPreprocessAudio()
        {
            if (!assetPath.Contains("Resources/Audio/")) return;
            var imp = (AudioImporter)assetImporter;
            bool music = assetPath.Contains("/Music/");
            var s = imp.defaultSampleSettings;
            if (music)
            {
                s.loadType = AudioClipLoadType.Streaming;
                s.compressionFormat = AudioCompressionFormat.Vorbis;
                s.quality = 0.45f;
                s.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
                imp.loadInBackground = true;
            }
            else
            {
                s.loadType = AudioClipLoadType.DecompressOnLoad;
                s.compressionFormat = AudioCompressionFormat.Vorbis;
                s.quality = 0.6f;
                imp.forceToMono = true;
            }
            imp.defaultSampleSettings = s;
        }
    }
}
