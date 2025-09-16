using UnityEngine;
using Sirenix.OdinInspector;

public class MaterialSwitcher : MonoBehaviour
{
    [SerializeField] private Material mat1;
    [SerializeField] private Material mat2;

    private Renderer _renderer;

    private void Awake()
    {
        _renderer = GetComponent<Renderer>();
    }
    [Button]
    public void SwitchTo(int type)
    {
        if (_renderer == null) Awake();
        if (_renderer == null) return;

        switch (type)
        {
            case 0:
                if (mat1 != null) _renderer.material = mat1;
                break;
            case 1:
                if (mat2 != null) _renderer.material = mat2;
                break;
        }
    }

    [Button]
    public void Toggle()
    {
        if (_renderer == null) Awake();
        if (_renderer == null) return;

        if (_renderer.material == mat1 && mat2 != null)
        {
            _renderer.material = mat2;
        }
        else if (_renderer.material == mat2 && mat1 != null)
        {
            _renderer.material = mat1;
        }
    }

}
