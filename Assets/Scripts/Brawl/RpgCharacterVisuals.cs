using UnityEngine;
using UnityEngine.Rendering;

namespace BrawlArena
{
    /// <summary>
    /// Adds a lightweight school crest to elemental mage rigs. Non-mage heroes
    /// keep their authored silhouette in the live match.
    /// </summary>
    public static class RpgCharacterVisuals
    {
        public static void Attach(BrawlerController owner)
        {
            if (owner == null || owner.specialty.school == SpellSchool.None ||
                owner.GetComponent<RpgCharacterAdornment>() != null) return;
            owner.gameObject.AddComponent<RpgCharacterAdornment>().Configure(owner.specialty.school);
        }
    }

    public sealed class RpgCharacterAdornment : MonoBehaviour
    {
        Transform crest;
        Material material;
        SpellSchool school;
        Vector3 restPosition;
        float phase;

        public void Configure(SpellSchool value)
        {
            school = value;
            phase = Random.value * Mathf.PI * 2f;
            Build();
        }

        void Build()
        {
            if (crest != null) Destroy(crest.gameObject);

            var root = new GameObject("RPG Class Crest");
            root.transform.SetParent(transform, false);
            crest = root.transform;
            restPosition = Vector3.zero;

            material = CreateMaterial(SchoolColor(school));
            switch (school)
            {
                case SpellSchool.Arcane:
                    BuildHalo();
                    break;
                case SpellSchool.Fire:
                    BuildFireCrown();
                    break;
                case SpellSchool.Frost:
                    BuildIceCrown();
                    break;
                case SpellSchool.Storm:
                    BuildOrbitingMotes(3, 0.48f, 1.72f, 0.13f);
                    break;
                case SpellSchool.Earth:
                    AddAdornment(PrimitiveType.Cube, "Left Runestone",
                        new Vector3(-0.5f, 1.48f, 0f), new Vector3(0.24f, 0.38f, 0.28f),
                        Quaternion.Euler(18f, 0f, 32f));
                    AddAdornment(PrimitiveType.Cube, "Right Runestone",
                        new Vector3(0.5f, 1.48f, 0f), new Vector3(0.24f, 0.38f, 0.28f),
                        Quaternion.Euler(-18f, 0f, -32f));
                    break;
                case SpellSchool.Poison:
                    BuildOrbitingMotes(4, 0.43f, 1.58f, 0.16f);
                    AddAdornment(PrimitiveType.Sphere, "Plague Core",
                        new Vector3(0f, 1.86f, 0.12f), Vector3.one * 0.2f,
                        Quaternion.identity);
                    break;
                case SpellSchool.Void:
                    BuildOrbitingMotes(3, 0.42f, 1.65f, 0.14f);
                    break;
            }
        }

        void BuildHalo()
        {
            const int count = 7;
            for (int i = 0; i < count; i++)
            {
                float angle = i / (float)count * Mathf.PI * 2f;
                AddAdornment(PrimitiveType.Sphere, "Sanctuary Halo " + i,
                    new Vector3(Mathf.Cos(angle) * 0.4f, 2.08f,
                        Mathf.Sin(angle) * 0.4f), Vector3.one * 0.105f,
                    Quaternion.identity);
            }
        }

        void BuildFireCrown()
        {
            AddAdornment(PrimitiveType.Cube, "Left Ember Horn",
                new Vector3(-0.32f, 1.96f, 0.02f), new Vector3(0.13f, 0.42f, 0.13f),
                Quaternion.Euler(0f, 0f, -24f));
            AddAdornment(PrimitiveType.Cube, "Crown Flame",
                new Vector3(0f, 2.16f, 0.02f), new Vector3(0.14f, 0.5f, 0.14f),
                Quaternion.Euler(0f, 45f, 0f));
            AddAdornment(PrimitiveType.Cube, "Right Ember Horn",
                new Vector3(0.32f, 1.96f, 0.02f), new Vector3(0.13f, 0.42f, 0.13f),
                Quaternion.Euler(0f, 0f, 24f));
        }

        void BuildIceCrown()
        {
            for (int i = -1; i <= 1; i++)
            {
                float height = i == 0 ? 0.46f : 0.34f;
                AddAdornment(PrimitiveType.Cube, "Ice Spire " + (i + 1),
                    new Vector3(i * 0.27f, 2.03f + height * 0.25f, 0.03f),
                    new Vector3(0.12f, height, 0.12f),
                    Quaternion.Euler(0f, 45f, i * -10f));
            }
        }

        void BuildOrbitingMotes(int count, float radius, float height, float size)
        {
            for (int i = 0; i < count; i++)
            {
                float angle = i / (float)count * Mathf.PI * 2f;
                AddAdornment(PrimitiveType.Sphere, "Orbiting Rune " + i,
                    new Vector3(Mathf.Cos(angle) * radius,
                        height + (i % 2 == 0 ? 0.1f : -0.08f),
                        Mathf.Sin(angle) * radius), Vector3.one * size,
                    Quaternion.identity);
            }
        }

        void AddAdornment(PrimitiveType primitive, string objectName, Vector3 localPosition,
            Vector3 localScale, Quaternion localRotation)
        {
            GameObject piece = GameObject.CreatePrimitive(primitive);
            piece.name = objectName;
            piece.transform.SetParent(crest, false);
            piece.transform.localPosition = localPosition;
            piece.transform.localScale = localScale;
            piece.transform.localRotation = localRotation;

            Collider collider = piece.GetComponent<Collider>();
            if (collider != null)
            {
                collider.enabled = false;
                Destroy(collider);
            }
            Renderer renderer = piece.GetComponent<Renderer>();
            if (renderer == null) return;
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        void LateUpdate()
        {
            if (crest == null) return;
            float speed = school == SpellSchool.Storm ? 90f :
                school == SpellSchool.Poison || school == SpellSchool.Void ? 46f : 24f;
            crest.Rotate(Vector3.up, speed * Time.deltaTime, Space.Self);
            if (school == SpellSchool.Arcane || school == SpellSchool.Storm ||
                school == SpellSchool.Poison || school == SpellSchool.Void)
            {
                Vector3 pos = restPosition;
                pos.y = Mathf.Sin(Time.time * 2.2f + phase) * 0.035f;
                crest.localPosition = pos;
            }
        }

        void OnDestroy()
        {
            if (material != null) Destroy(material);
        }

        static Material CreateMaterial(Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader == null) return null;

            var result = new Material(shader) { name = "RPG School Crest (Runtime)" };
            if (result.HasProperty("_BaseColor")) result.SetColor("_BaseColor", color);
            if (result.HasProperty("_Color")) result.SetColor("_Color", color);
            if (result.HasProperty("_EmissionColor"))
            {
                result.EnableKeyword("_EMISSION");
                result.SetColor("_EmissionColor", color * 2.2f);
            }
            return result;
        }

        static Color SchoolColor(SpellSchool value)
        {
            switch (value)
            {
                case SpellSchool.Arcane: return Hex("E8FFB0");
                case SpellSchool.Fire: return Hex("FF5A1F");
                case SpellSchool.Frost: return Hex("84EFFF");
                case SpellSchool.Storm: return Hex("B993FF");
                case SpellSchool.Earth: return Hex("B7DB68");
                case SpellSchool.Poison: return Hex("7CFF38");
                case SpellSchool.Void: return Hex("D34CFF");
                default: return Color.white;
            }
        }

        static Color Hex(string value)
        {
            return ColorUtility.TryParseHtmlString("#" + value, out Color color)
                ? color
                : Color.white;
        }
    }
}
