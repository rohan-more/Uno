using UnityEngine;

/// <summary>
/// The avatar sprites, in the order the server indexes them. The server stores
/// only a number per account, so this asset decides what that number looks like.
/// Create one via Assets > Create > UNO > Avatar Library and drop the sprites in.
/// </summary>
[CreateAssetMenu(fileName = "AvatarLibrary", menuName = "UNO/Avatar Library")]
public class AvatarLibrary : ScriptableObject
{
    [SerializeField] private Sprite[] avatars;

    /// <summary>How many avatars exist. The server picks an index below this.</summary>
    public int Count => avatars != null ? avatars.Length : 0;

    /// <summary>
    /// Sprite for a server-provided index. Out-of-range values wrap around, so a
    /// server that knows about more avatars than this build still shows a face.
    /// </summary>
    public Sprite Get(int index)
    {
        if (Count == 0)
            return null;

        return avatars[((index % Count) + Count) % Count];
    }
}
