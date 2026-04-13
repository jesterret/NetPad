import {ScriptKind} from "@application";

export class LangLogoValueConverter {
    public toView(scriptKind: ScriptKind): string | undefined {
        return LangLogoValueConverter.resolveIconImageSrc(scriptKind);
    }

    public static resolveIconImageSrc(kind: ScriptKind): string | undefined {
        if (kind === "Program" || kind === "Expression") return "img/csharp-logo.png";
        if (kind === "SQL") return "img/sql-logo.svg";
        return undefined;
    }
}
