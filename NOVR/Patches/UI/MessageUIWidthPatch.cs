using HarmonyLib;
using NOVR.PatchHelper;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace NOVR.Patches.UI;

// The chat/message box (ChatCanvas > ChatBackground) sizes itself with a ContentSizeFitter set to the preferred
// width of its VerticalLayoutGroup, and TMP reports a text's preferred width as its longest unwrapped line. So one
// long server message stretches the box across the HUD (InfernoSuperNova/novr#41) even though the HQMessages and
// KillFeed texts already have word wrapping on. Capping the preferred width lets TMP wrap the rest onto new lines.
internal static class MessageUIWidthPatch
{
    // ChatCanvas's TopPanel reserves a 500 px wide column (LeftSpace) for the chat box, about as wide as the chat
    // input row, so wrap there.
    private const float MaxTextWidth = 500f;

    private static readonly AccessTools.FieldRef<MessageUI, TextMeshProUGUI> MessageTextField =
        AccessTools.FieldRefAccess<MessageUI, TextMeshProUGUI>("messageText");

    private static readonly AccessTools.FieldRef<MessageUI, TextMeshProUGUI> KillFeedTextField =
        AccessTools.FieldRefAccess<MessageUI, TextMeshProUGUI>("killFeedText");

    [PatchPostfix(typeof(MessageUI), "Awake")]
    private static void Awake_Postfix(MessageUI __instance)
    {
        CapWidth(MessageTextField(__instance));
        CapWidth(KillFeedTextField(__instance));
    }

    private static void CapWidth(TextMeshProUGUI text)
    {
        if (text == null || text.GetComponent<MaxPreferredWidth>() != null) return;

        text.enableWordWrapping = true;
        var cap = text.gameObject.AddComponent<MaxPreferredWidth>();
        cap.Text = text;
        cap.MaxWidth = MaxTextWidth;
    }
}

// Reports min(text preferred width, MaxWidth) as the preferred width, and nothing else. Its layout priority is above
// both TMP (0) and the prefab's LayoutElement (1), so layout groups use it; every other property returns -1 so they
// fall through to those components as before.
internal class MaxPreferredWidth : UIBehaviour, ILayoutElement
{
    public TMP_Text? Text;
    public float MaxWidth;

    public float preferredWidth => Text != null ? Mathf.Min(Text.preferredWidth, MaxWidth) : -1f;
    public int layoutPriority => 2;

    public float minWidth => -1f;
    public float flexibleWidth => -1f;
    public float minHeight => -1f;
    public float preferredHeight => -1f;
    public float flexibleHeight => -1f;

    public void CalculateLayoutInputHorizontal() { }
    public void CalculateLayoutInputVertical() { }
}
