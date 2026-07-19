GAME THUMBNAIL / COVER ART
==========================

Drop game cover images in THIS folder. They show up as the game's thumbnail on
the game detail screen and grow out during the "swallow" launch animation.

Use these exact filenames (the sample games already point at them):

    cs2.png            -> Counter-Strike 2
    fortnite.png       -> Fortnite
    cyberpunk2077.png  -> Cyberpunk 2077
    eldenring.png      -> Elden Ring

Format:  PNG or JPG both work.
Shape:   Portrait cover art (roughly 3:4, e.g. 600 x 800) looks best. The image
         is scaled with UniformToFill, so it crops to fill the thumbnail either way.

After adding the files, just relaunch the app (dotnet run) - they are copied to
the build output automatically, no code change needed.
