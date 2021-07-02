If you want to build and run this against actual DDLC+:
1. Download https://github.com/BepInEx/BepInEx/releases/tag/v5.4.11 and extract it into your DDLC+ directory
2. Grab all of the DLLs from the new BepInEx/core directory and put them in the Libs folder of this repo
3. Grab all of the DLLs from the "Doki Doki Literature Club Plus_Data/Managed" directory and put them in the Libs folder of this repo
4. If you're not running in Steam under Windows, you probably need to update the OutDir property in TestDDLCMod.csproj to point to the BepInEx\plugins subdirectory of your DDLC+ install
5. At this point, you should be able to build and run the solution - lmk if that doesn't work for you
