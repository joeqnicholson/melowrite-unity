using Melowrite;
using Melowrite.Audio;
using UnityEngine;
using UnityEngine.UI;

public class Music : MonoBehaviour
{
    public MeloFile indoFile;
    public MeloFile carnivalFile;
    private MeloSource player;
    public Button Now;
    public Button Bar;
    public Button Fade;
    public Button CrossFade;
    public Slider VolumeSlider;
    public Slider TempoSlider;
    
    
    void Start()
    {
        Melo.LoadAsync(carnivalFile);

        player = GetComponent<MeloSource>();
        player.Load(indoFile);
        player.PlayChunk(0);
        
        Now.onClick.AddListener(SwitchSongNow);
        Bar.onClick.AddListener(SwitchSongBar);
        CrossFade.onClick.AddListener(CrossFadeSong);
        Fade.onClick.AddListener(FadeOutSong);
    }

    void Update()
    {
        player.SetMasterVolume(VolumeSlider.value);
        player.SetTempo((int)TempoSlider.value);
    }

    public void SwitchSongNow()
    {
        MeloFile next = player.File == indoFile ? carnivalFile : indoFile;
        player.SwitchSong(next, 0, MeloSwitch.Now, 0f);
    }
    
    public void SwitchSongBar()
    {
        MeloFile next = player.File == indoFile ? carnivalFile : indoFile;
        player.SwitchSong(next, 0, MeloSwitch.Bar, 0f);
    }
    
    public void CrossFadeSong()
    {
        MeloFile next = player.File == indoFile ? carnivalFile : indoFile;
        player.SwitchSongCrossfade(next, 0, 2,MeloSwitch.Now);
    }
    
    public void FadeOutSong()
    {
        MeloFile next = player.File == indoFile ? carnivalFile : indoFile;
        player.SwitchSong(next, 1,MeloSwitch.Now, 2);
    }
    
    
    
}
