# RimWorld Harmony Library Mod

This mod brings the Harmony library into RimWorld modding.

![Harmony](https://raw.githubusercontent.com/pardeike/Harmony/master/HarmonyLogo.png)  
GitHub Repository: [Harmony](https://github.com/pardeike/Harmony)

Instead of including `0Harmony.dll` in your `Assemblies` folder, you use the Harmony reference from nuget: **Lib.Harmony.Ref**. Then, you add the following to your `About.xml` file:

```
<modDependencies>
    <li>
        <packageId>brrainz.harmony</packageId>
        <displayName>Harmony</displayName>
        <steamWorkshopUrl>steam://url/CommunityFilePage/2009463077</steamWorkshopUrl>
        <downloadUrl>https://github.com/pardeike/HarmonyRimWorld/releases/latest</downloadUrl>
    </li>
</modDependencies>
```

which will make RimWorld force the user to install this mod. It will automatically want to be installed high up in the list which makes it supply `HarmonyLib` to all mods below. This means that all mods will use **the same Harmony version**.

Whenever Harmony needs updating, this mod will update too.

**Notes**

Adding **HARMONY_NO_LOG** (any non-empty value will do) to your environment variables will suppress the creation of the `harmony.log.txt` file that is created if some mod author forgets to remove debugging before releasing their mod. See also [How to edit environment variables](https://www.howtogeek.com/787217/how-to-edit-environment-variables-on-windows-10-or-11/).

If you want to turn off the enhancement of stacktraces, you can edit the tweak values of RimWorld (use Debug mode, then the second debug icon from the left) and check the following tweak variables:

<img width="820" height="60" alt="image" src="https://github.com/user-attachments/assets/322f0b9f-0bdc-4aa7-8796-603195275865" />

---

**Support**

Supporting me will help me to help the community. On [Patreon](https://patreon.com/pardeike) or on [GitHub](https://github.com/sponsors/pardeike).  
Or simply star my GitHub repository (its free!): https://github.com/pardeike/Harmony

ENJOY  
/Brrainz

