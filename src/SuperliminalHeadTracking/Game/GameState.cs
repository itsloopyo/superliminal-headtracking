namespace SuperliminalHeadTracking.Game
{
    public enum GameState
    {
        /// <summary>No level loaded, or the player rig has not been attached yet.</summary>
        Loading,

        /// <summary>Free-look gameplay. The only state that gets tracking.</summary>
        Gameplay,

        /// <summary>Pause menu, escape menu, options, level select, the dream editor UI.</summary>
        Menu,

        /// <summary>A scripted sequence owns the camera, or the credits are rolling.</summary>
        Cutscene,

        /// <summary>Connected to Photon: the multiplayer menu, a lobby, or a match.</summary>
        Multiplayer
    }
}
