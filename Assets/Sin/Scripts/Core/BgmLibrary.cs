using System;
using System.Collections.Generic;
using UnityEngine;

namespace Factory.Audio
{
    // 씬 이름 -> BGM 곡 대응표. BgmManager가 씬이 로드될 때 이 표에서 그 씬의 곡을 찾아 튼다.
    // 코드 수정 없이 곡만 바꿔 끼울 수 있게 ScriptableObject로 뺐다 — 반드시 Resources 폴더 안에
    // "BgmLibrary"라는 이름으로 둬야 한다(BgmManager가 Resources.Load로 찾는다). 메뉴
    // Tools > Audio > Create BGM Library로 만들면 자리(Assets/Resources)와 이름이 자동으로 맞는다.
    [CreateAssetMenu(fileName = "BgmLibrary", menuName = "Factory/BGM Library")]
    public sealed class BgmLibrary : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            [Tooltip("Build Settings에 등록된 씬 이름(예: Title, Main)")]
            public string sceneName;
            public AudioClip clip;
            [Range(0f, 1f)] public float volume = 1f;
        }

        public List<Entry> entries = new List<Entry>();

        [Tooltip("새 곡(또는 광고/음소거 뒤 원래 소리)이 0에서 천천히 커지는 시간(초)")]
        [Min(0.01f)] public float fadeInSeconds = 2.5f;

        [Tooltip("곡이 바뀔 때 이전 곡이 천천히 작아져서 사라지는 시간(초)")]
        [Min(0.01f)] public float fadeOutSeconds = 2.5f;

        [Tooltip("광고가 뜨거나 음소거할 때 소리를 줄이는 시간(초). 너무 길면 광고 소리와 겹친다.")]
        [Min(0.01f)] public float duckSeconds = 0.6f;

        [Tooltip("표에 없는 씬으로 넘어갔을 때 음악을 끌지. 꺼두면 앞 씬 곡이 그대로 이어진다.")]
        public bool stopWhenSceneUnmapped;

        public bool TryGet(string sceneName, out Entry entry)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i] != null && entries[i].sceneName == sceneName)
                {
                    entry = entries[i];
                    return true;
                }
            }
            entry = null;
            return false;
        }
    }
}
