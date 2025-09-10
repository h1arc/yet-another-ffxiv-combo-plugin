using yetanotherffxivcomboplugin.src.Core;

namespace yetanotherffxivcomboplugin.src.Jobs.SGE;

public static class SGEOpeners
{
    // Based on plan: Toxikon opener sequence; countdown handling is external.
    public static readonly Opener ToxikonOpener = new(
        new Opener.Step(SGEIDs.Eukrasia, true),
        new Opener.Step(SGEIDs.ToxikonI, true),
        new Opener.Step(SGEIDs.EukrasianDosisIII, true),
        new Opener.Step(SGEIDs.DosisIII, true),
        new Opener.Step(SGEIDs.DosisIII, true),
        new Opener.Step(SGEIDs.DosisIII, true),
        new Opener.Step(SGEIDs.PhlegmaIII, true),
        new Opener.Step(SGEIDs.PhlegmaIII, true),
        new Opener.Step(SGEIDs.DosisIII, true),
        new Opener.Step(SGEIDs.DosisIII, true),
        new Opener.Step(SGEIDs.DosisIII, true),
        new Opener.Step(SGEIDs.DosisIII, true),
        new Opener.Step(SGEIDs.Eukrasia, true),
        new Opener.Step(SGEIDs.EukrasianDosisIII, true),
        new Opener.Step(SGEIDs.DosisIII, true),
        new Opener.Step(SGEIDs.DosisIII, true),
        new Opener.Step(SGEIDs.DosisIII, true)
    );
}
