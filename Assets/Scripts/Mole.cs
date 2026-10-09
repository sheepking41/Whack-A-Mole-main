using System.Collections;
using UnityEngine;

public class Mole : MonoBehaviour {
  [Header("Graphics")]
  [SerializeField] private Sprite mole;
  [SerializeField] private Sprite moleHardHat;
  [SerializeField] private Sprite moleHatBroken;
  [SerializeField] private Sprite moleHit;
  [SerializeField] private Sprite moleHatHit;

  [Header("Explosion Effect")]
  [SerializeField] private GameObject explosionPrefab;
  [SerializeField] private Sprite explosionSprite;
  [SerializeField] private float explosionDuration = 0.3f;

  [Header("GameManager")]
  [SerializeField] private GameManager gameManager;

  // The offset of the sprite to hide it.
  private Vector2 startPosition = new Vector2(0f, -2.56f);
  private Vector2 endPosition = Vector2.zero;
  // How long it takes to show a mole.
  private float showDuration = 0.5f;
  private float duration = 1f;

  private SpriteRenderer spriteRenderer;
  private Animator animator;
  private BoxCollider2D boxCollider2D;
  private Vector2 boxOffset;
  private Vector2 boxSize;
  private Vector2 boxOffsetHidden;
  private Vector2 boxSizeHidden;

  // Mole Parameters 
  private bool hittable = true;
  public enum MoleType { Standard, HardHat, Bomb };
  private MoleType moleType;
  private float hardRate = 0.25f;
  private float bombRate = 0f;
  private int lives;
  private int moleIndex = 0;

  // Procedural sprites built in Awake. Instance fields (NOT static!) so Unity never unloads them.
  private Sprite _procSmokeSprite;
  private Sprite _procFlashSprite;
  private Sprite _procEmberSprite;

  private IEnumerator ShowHide(Vector2 start, Vector2 end) {
    // Make sure we start at the start.
    transform.localPosition = start;

    // Show the mole.
    float elapsed = 0f;
    while (elapsed < showDuration) {
      transform.localPosition = Vector2.Lerp(start, end, elapsed / showDuration);
      boxCollider2D.offset = Vector2.Lerp(boxOffsetHidden, boxOffset, elapsed / showDuration); //move the collider box from offset = 1.28 (start at the upper edge of the mole) to offset = 0 (ends at center of the mole, i.e. same as the mole itself), same as moving from y = -2.56(startPosition.y)+1.28(offset) = -1.28 to y = 0
      boxCollider2D.size = Vector2.Lerp(boxSizeHidden, boxSize, elapsed / showDuration); //gradually increase size from zero to full box size
      // Update at max framerate.
      elapsed += Time.deltaTime;
      yield return null;
    }

    // Make sure we're exactly at the end.
    transform.localPosition = end;
    boxCollider2D.offset = boxOffset;
    boxCollider2D.size = boxSize;

    // Wait for duration to pass.
    yield return new WaitForSeconds(duration);

    // Hide the mole.
    elapsed = 0f;
    while (elapsed < showDuration) {
      transform.localPosition = Vector2.Lerp(end, start, elapsed / showDuration);
      boxCollider2D.offset = Vector2.Lerp(boxOffset, boxOffsetHidden, elapsed / showDuration);
      boxCollider2D.size = Vector2.Lerp(boxSize, boxSizeHidden, elapsed / showDuration);
      // Update at max framerate.
      elapsed += Time.deltaTime;
      yield return null;
    }
    // Make sure we're exactly back at the start position.
    transform.localPosition = start;
    boxCollider2D.offset = boxOffsetHidden;
    boxCollider2D.size = boxSizeHidden;

    // If we got to the end and it's still hittable then we missed it.
    if (hittable) {
      hittable = false;
      // We only give time penalty if it isn't a bomb. If it is a bomb, missing it will not have penalty.
      gameManager.Missed(moleIndex, moleType != MoleType.Bomb);
    }
  }

  public void Hide() {
    // Set the appropriate mole parameters to hide it.
    transform.localPosition = startPosition;
    boxCollider2D.offset = boxOffsetHidden;
    boxCollider2D.size = boxSizeHidden;
  }

  private IEnumerator QuickHide() {
    yield return new WaitForSeconds(0.25f);
    // Whilst we were waiting we may have spawned again here, so just
    // check that hasn't happened before hiding it. This will stop it
    // flickering in that case. E.g. if spawn here again, the mole will be moved to the start position due to ShowHide coroutine, then it will be moving up (hittable = true in the CreateNext method) and then will be hidden suddenly if did not use the condition if(!hittable):
    if (!hittable) {
      Hide();
    }
  }

  private void SpawnExplosion() {
    // Spawn at the mole's CURRENT world position when hit (not the hypothetical endPosition),
    // so even if you tap mid-pop-up the explosion lines up correctly.
    Vector3 spawnPos = transform.position;

    // Priority 1: Use assigned prefab (e.g. particle system)
    if (explosionPrefab != null) {
      GameObject explosion = Instantiate(explosionPrefab, spawnPos, Quaternion.identity);
      // Try to push any SpriteRenderer child to render in front of the mole on the same layer
      SpriteRenderer prefabSr = explosion.GetComponentInChildren<SpriteRenderer>();
      if (prefabSr != null && spriteRenderer != null) {
        prefabSr.sortingLayerID = spriteRenderer.sortingLayerID;
        prefabSr.sortingOrder = spriteRenderer.sortingOrder + 10;
      }
      Destroy(explosion, explosionDuration);
      return;
    }

    // Priority 2: Sprite provided — wrap it with smoke rings and sparks for a proper bomb feel.
    if (explosionSprite != null) {
      StartCoroutine(BombExplosionCoroutine(spawnPos, explosionSprite));
      return;
    }

    // Priority 3: Full fallback bomb explosion (smoke rings, bright flash, spark embers, shockwave)
    StartCoroutine(BombExplosionCoroutine(spawnPos, null));
  }

  // ------------------------------------------------------------------------------------------
  // Procedural soft-circle sprite generator.
  // Caller owns the returned Sprite (and its Texture2D) — protect with HideFlags if needed.
  // ------------------------------------------------------------------------------------------
  private static Sprite MakeSoftCircleSprite(int size, Color coreColor) {
    Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
    tex.filterMode = FilterMode.Bilinear;
    tex.wrapMode = TextureWrapMode.Clamp;

    float half = (size - 1) * 0.5f;
    for (int y = 0; y < size; y++) {
      for (int x = 0; x < size; x++) {
        float dx = (x - half) / half;
        float dy = (y - half) / half;
        float dist = Mathf.Sqrt(dx * dx + dy * dy);
        if (dist > 1f) {
          tex.SetPixel(x, y, new Color(0, 0, 0, 0));
        } else {
          // soft edge: core stays opaque, edges fade out
          float alpha = 1f - Mathf.Pow(dist, 1.7f);
          Color c = coreColor * Mathf.Lerp(1f, 0.7f, dist);
          c.a = alpha;
          tex.SetPixel(x, y, c);
        }
      }
    }
    tex.Apply();
    Sprite s = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    return s;
  }

  // Bulletproof: never return null for explosion sprite input. If the user provided a custom
  // sprite or Awake built one, use it. Otherwise, fall back to copying the mole's current
  // hit sprite (which is always populated in normal gameplay).
  private Sprite SafeExplosionSprite(Sprite preferred, Sprite fallbackProc) {
    if (preferred != null) return preferred;
    if (fallbackProc != null && fallbackProc.texture != null) return fallbackProc;
    if (spriteRenderer != null && spriteRenderer.sprite != null) return spriteRenderer.sprite;
    return fallbackProc; // final last-ditch (may be null only if literally no sprites exist)
  }

  // One-shot helper: creates a child sprite renderer on a parent with given layer/order settings.
  private SpriteRenderer MakeBurstChild(GameObject parent, string name, Sprite sprite, int sortingOffset, int moleSortingLayer, int moleSortingOrder) {
    GameObject go = new GameObject(name);
    go.transform.SetParent(parent.transform, false);
    go.transform.localPosition = Vector3.zero;

    SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
    // Guarantee a non-null sprite. If sprite is null, fall back to Awake's built proc; if that
    // is also null for some reason, fall back to mole's visible hit sprite.
    sr.sprite = SafeExplosionSprite(sprite, _procSmokeSprite);
    sr.sortingLayerID = moleSortingLayer;
    sr.sortingOrder = moleSortingOrder + sortingOffset;
    return sr;
  }

  // ------------------------------------------------------------------------------------------
  // Full bomb-style explosion: flash core, 3 smoke rings, spark embers, shockwave tint.
  // Lasts exactly explosionDuration (0.3s by default) — but visually looks bigger & juicier.
  // ------------------------------------------------------------------------------------------
  private IEnumerator BombExplosionCoroutine(Vector3 spawnPos, Sprite customSprite) {
    // If Awake somehow didn't build the proc sprites (edge case), build them RIGHT NOW.
    if (_procSmokeSprite == null || _procSmokeSprite.texture == null) {
      _procSmokeSprite = MakeSoftCircleSprite(96, Color.white);
      if (_procSmokeSprite != null) _procSmokeSprite.texture.hideFlags = HideFlags.DontSave;
    }
    if (_procFlashSprite == null || _procFlashSprite.texture == null) {
      _procFlashSprite = MakeSoftCircleSprite(96, new Color(1f, 1f, 0.65f));
      if (_procFlashSprite != null) _procFlashSprite.texture.hideFlags = HideFlags.DontSave;
    }
    if (_procEmberSprite == null || _procEmberSprite.texture == null) {
      _procEmberSprite = MakeSoftCircleSprite(20, new Color(1f, 0.9f, 0.25f));
      if (_procEmberSprite != null) _procEmberSprite.texture.hideFlags = HideFlags.DontSave;
    }

    // Root explosion anchor (keeps everything together, easy to destroy)
    GameObject root = new GameObject("BombExplosion");
    root.transform.position = spawnPos;

    int sortLayer = spriteRenderer != null ? spriteRenderer.sortingLayerID : 0;
    int sortBase  = spriteRenderer != null ? spriteRenderer.sortingOrder : 0;
    // Reference mole scale so explosions stay proportional to the gameplay art.
    // Declared HERE (before layer init blocks) because both initial-scale setup AND the
    // timing loop below depend on it.
    float baseScale = spriteRenderer != null ? spriteRenderer.transform.lossyScale.x : 1f;

    // --- Layer 0: FIRE (inside the plume). Glows yellow→orange→deep red, lingers behind smoke.
    // Sits between the front/back smoke puffs in sorting order so it appears "inside" the cloud.
    Sprite fireSprite = customSprite != null ? customSprite : _procFlashSprite;
    SpriteRenderer fireY = MakeBurstChild(root, "FireYellow",  fireSprite, 16, sortLayer, sortBase);
    SpriteRenderer fireO = MakeBurstChild(root, "FireOrange",  fireSprite, 15, sortLayer, sortBase);
    SpriteRenderer fireR = MakeBurstChild(root, "FireDeepRed", fireSprite, 14, sortLayer, sortBase);
    // Fire starts at mole-face level (same anchor as smoke) so it appears to pour out of the hole
    fireY.transform.localPosition = new Vector3( 0.00f, 0.05f, 0);
    fireO.transform.localPosition = new Vector3( 0.06f,-0.02f, 0);
    fireR.transform.localPosition = new Vector3(-0.05f, 0.00f, 0);
    fireY.transform.localScale = Vector3.one * baseScale * 0.9f;
    fireO.transform.localScale = Vector3.one * baseScale * 1.1f;
    fireR.transform.localScale = Vector3.one * baseScale * 1.25f;
    Color fireYStart = new Color(1.00f, 0.98f, 0.35f, 1.00f); // bright yellow core
    Color fireOStart = new Color(1.00f, 0.55f, 0.10f, 0.95f); // orange middle
    Color fireRStart = new Color(0.75f, 0.12f, 0.05f, 0.90f); // deep red outer fire
    Color fireEnd    = new Color(0.60f, 0.10f, 0.00f, 0.00f); // fade out hot red

    // --- Layer 1: BRIGHT CORE FLASH (top layer, biggest at start, fades fast) ------------
    Sprite flashSprite = customSprite != null ? customSprite : _procFlashSprite;
    SpriteRenderer flashSr = MakeBurstChild(root, "Flash", flashSprite, 20, sortLayer, sortBase);
    flashSr.transform.localScale = Vector3.one * baseScale * 1.3f;
    Color flashStart = Color.white;

    // --- Layer 2: THREE SMOKE PUFFS (rise like steam + cover mole's face at spawn) --------
    Sprite smokeSprite = customSprite != null ? customSprite : _procSmokeSprite;
    // Smoke1 is the FRONT, big central plume. Starts CENTERED on the mole (0,0) so it
    // INSTANTLY covers the mole face before rising. Smoke2/Smoke3 flank it with tiny offsets.
    SpriteRenderer smokeSr1 = MakeBurstChild(root, "Smoke1", smokeSprite, 19, sortLayer, sortBase); // front (in front of flash)
    SpriteRenderer smokeSr2 = MakeBurstChild(root, "Smoke2", smokeSprite, 17, sortLayer, sortBase); // mid  (in FRONT of fire so fire peeks only from gaps)
    SpriteRenderer smokeSr3 = MakeBurstChild(root, "Smoke3", smokeSprite, 13, sortLayer, sortBase); // back (behind fire so fire shows through rear gap)
    Vector3 s1Start = new Vector3( 0.00f,  0.00f, 0); // CENTERED: hides the mole face instantly
    Vector3 s2Start = new Vector3( 0.22f,  0.05f, 0); // flanking puffs: small offsets so they
    Vector3 s3Start = new Vector3(-0.20f, -0.03f, 0); //   merge with the central plume
    // Where each puff ends: rises UPWARD like steam (2.0 – 2.6 × baseScale higher)
    float smokeRise1 = 2.6f * baseScale;
    float smokeRise2 = 2.0f * baseScale;
    float smokeRise3 = 2.3f * baseScale;
    Vector3 s1End = s1Start + new Vector3(-0.05f, smokeRise1, 0); // slight drift left as it rises
    Vector3 s2End = s2Start + new Vector3( 0.18f, smokeRise2, 0); // drifts right + up
    Vector3 s3End = s3Start + new Vector3(-0.20f, smokeRise3, 0); // drifts left + up
    smokeSr1.transform.localPosition = s1Start;
    smokeSr2.transform.localPosition = s2Start;
    smokeSr3.transform.localPosition = s3Start;
    // Smoke starts ALREADY large enough to cover the mole face on frame 1
    smokeSr1.transform.localScale = Vector3.one * baseScale * 1.40f;
    smokeSr2.transform.localScale = Vector3.one * baseScale * 1.15f;
    smokeSr3.transform.localScale = Vector3.one * baseScale * 1.25f;
    // Fire RISE targets (fire rises slower than smoke, so it lags inside the plume — realistic)
    float fireRise = 1.4f * baseScale;
    Vector3 fyStart = fireY.transform.localPosition;
    Vector3 foStart = fireO.transform.localPosition;
    Vector3 frStart = fireR.transform.localPosition;
    Vector3 fyEnd = fyStart + new Vector3(0, fireRise * 1.05f, 0);
    Vector3 foEnd = foStart + new Vector3(0, fireRise * 0.95f, 0);
    Vector3 frEnd = frStart + new Vector3(0, fireRise * 0.85f, 0);
    // Smoke starts dark gray-brown (right off the mole), fades to pale gray transparent at top
    Color smoke1Start = new Color(0.50f, 0.45f, 0.50f, 0.96f);
    Color smoke2Start = new Color(0.48f, 0.43f, 0.48f, 0.92f);
    Color smoke3Start = new Color(0.42f, 0.38f, 0.45f, 0.90f);
    Color smokeEnd    = new Color(0.88f, 0.88f, 0.90f, 0.0f);

    // --- Layer 3: EMBER SPARKS (biased UPWARD so they shoot out of the top of the plume) --
    int sparkCount = 8;
    SpriteRenderer[] sparks = new SpriteRenderer[sparkCount];
    Vector2[] sparkDirs = new Vector2[sparkCount];
    float[]   sparkSpeeds = new float[sparkCount];
    Sprite ember = customSprite != null ? customSprite : _procEmberSprite;
    for (int i = 0; i < sparkCount; i++) {
      // Most sparks fly UPWARD into the steam plume (cone of 110° centered on +Y axis).
      // A couple fly sideways/down slightly for a realistic detonation halo.
      float upwardBias;
      if (i < 6) upwardBias = Random.Range(-0.95f, 0.95f);  // upper 110° cone (mostly up)
      else       upwardBias = Random.Range( 0.30f, 0.95f);  // last 2: flatter side angles
      float angle = Mathf.Lerp(0f, Mathf.PI, (upwardBias + 1f) * 0.5f); // 0..PI = upper half
      if (i == 7) angle = -angle + 2f * Mathf.PI; // last one actually goes a bit down-right
      float dx = Mathf.Cos(angle);
      float dy = Mathf.Sin(angle);
      sparkDirs[i]   = new Vector2(dx, dy);
      sparkSpeeds[i] = Random.Range(1.0f, 2.2f); // a bit faster to punch through the rising smoke
      sparks[i] = MakeBurstChild(root, "Spark" + i, ember, 21 + i, sortLayer, sortBase);
      sparks[i].transform.localScale = Vector3.one * Random.Range(0.28f, 0.55f);
      sparks[i].transform.localPosition =
          new Vector3(dx, dy, 0) * (sparkSpeeds[i] * baseScale * 0.12f);
    }

    // --- Timing curves ---------------------------------------------------------------------
    float elapsed = 0f;
    float dur = explosionDuration;

    while (elapsed < dur) {
      float t = elapsed / dur; // 0..1 over 0.3s

      // --- Flash: HUGE at t=0, shrink fast, fade in ~half the burst
      float flashT = Mathf.Clamp01(t / 0.55f);
      flashSr.transform.localScale = Vector3.Lerp(Vector3.one * baseScale * 1.7f, Vector3.one * baseScale * 0.3f, flashT);
      flashSr.color = Color.Lerp(flashStart, new Color(1, 1, 1, 0), flashT);

      // --- FIRE (inner plume): scales, fades, and RISES slower than the smoke (lags inside)
      float fyScale = Mathf.SmoothStep(0.90f, 2.80f, t) * baseScale;
      float foScale = Mathf.SmoothStep(1.10f, 3.10f, t) * baseScale;
      float frScale = Mathf.SmoothStep(1.25f, 3.40f, t) * baseScale;
      fireY.transform.localScale = Vector3.one * fyScale;
      fireO.transform.localScale = Vector3.one * foScale;
      fireR.transform.localScale = Vector3.one * frScale;
      // Rise motion (fire lags behind smoke with slower upward translation)
      float fireRiseT = Mathf.SmoothStep(0, 1, t);
      fireY.transform.localPosition = Vector3.Lerp(fyStart, fyEnd, fireRiseT);
      fireO.transform.localPosition = Vector3.Lerp(foStart, foEnd, fireRiseT);
      fireR.transform.localPosition = Vector3.Lerp(frStart, frEnd, fireRiseT);
      // Color: keep intense through ~60% then fade out fast
      float fireFadeT = Mathf.Clamp01(Mathf.Max(0f, (t - 0.35f) / 0.65f));
      fireY.color = Color.Lerp(fireYStart, fireEnd, fireFadeT);
      fireO.color = Color.Lerp(fireOStart, fireEnd, fireFadeT);
      fireR.color = Color.Lerp(fireRStart, fireEnd, fireFadeT);

      // --- SMOKE: expand to huge cloud, RISE upward, fade from dark to pale gray
      float s1 = Mathf.SmoothStep(1.40f, 4.70f, t) * baseScale;
      float s2 = Mathf.SmoothStep(1.15f, 4.10f, t) * baseScale;
      float s3 = Mathf.SmoothStep(1.25f, 4.40f, t) * baseScale;
      smokeSr1.transform.localScale = Vector3.one * s1;
      smokeSr2.transform.localScale = Vector3.one * s2;
      smokeSr3.transform.localScale = Vector3.one * s3;
      // RISE motion (steam feel, slight horizontal drift with t for plume sway)
      float riseT = Mathf.SmoothStep(0, 1, t);
      smokeSr1.transform.localPosition = Vector3.Lerp(s1Start, s1End, riseT);
      smokeSr2.transform.localPosition = Vector3.Lerp(s2Start, s2End, riseT);
      smokeSr3.transform.localPosition = Vector3.Lerp(s3Start, s3End, riseT);
      smokeSr1.color = Color.Lerp(smoke1Start, smokeEnd, t);
      smokeSr2.color = Color.Lerp(smoke2Start, smokeEnd, t);
      smokeSr3.color = Color.Lerp(smoke3Start, smokeEnd, t);

      // --- Sparks: fly (UPWARD biased), shift Y→O→R, shrink + fade
      for (int i = 0; i < sparkCount; i++) {
        float travelT = Mathf.SmoothStep(0, 1, t);
        sparks[i].transform.localPosition =
            new Vector3(sparkDirs[i].x, sparkDirs[i].y, 0) * (sparkSpeeds[i] * baseScale * travelT);
        Color c = sparks[i].color;
        c.a = 1f - t;
        c.r = 1f;
        c.g = Mathf.Lerp(0.95f, 0.25f, t);
        c.b = Mathf.Lerp(0.35f, 0.05f, t);
        sparks[i].color = c;
        sparks[i].transform.localScale *= (1f - t * 0.42f);
      }

      elapsed += Time.deltaTime;
      yield return null;
    }

    Destroy(root);
  }

  // Retained for backward-compatibility — unused internally now but left so nothing breaks
  // if someone called it from elsewhere.
  private IEnumerator SpriteExplosionCoroutine(Vector3 spawnPos) {
    StartCoroutine(BombExplosionCoroutine(spawnPos, explosionSprite));
    yield break;
  }
  private IEnumerator FallbackExplosionCoroutine(Vector3 spawnPos) {
    StartCoroutine(BombExplosionCoroutine(spawnPos, null));
    yield break;
  }

  private void OnMouseDown() {
    if (hittable) {
      switch (moleType) {
        case MoleType.Standard:
          spriteRenderer.sprite = moleHit;
          gameManager.AddScore(moleIndex); //moleIndex had been set by GameManager when the game starts. Passing the moleIndex to AddScore to remove the mole from the currentMoles list.
          // Stop the ShowHide animation FIRST — before starting explosion, otherwise StopAllCoroutines kills the explosion too.
          StopAllCoroutines();
          SpawnExplosion();
          StartCoroutine(QuickHide());
          // Turn off hittable so that we can't keep tapping for score.
          hittable = false;
          break;
        case MoleType.HardHat:
          // If lives == 2 reduce, and change sprite.
          if (lives == 2) {
            spriteRenderer.sprite = moleHatBroken;
            lives--;
          } else {
            spriteRenderer.sprite = moleHatHit;
            gameManager.AddScore(moleIndex);
            // Stop the ShowHide animation FIRST — before starting explosion, otherwise StopAllCoroutines kills the explosion too.
            StopAllCoroutines();
            SpawnExplosion();
            StartCoroutine(QuickHide());
            // Turn off hittable so that we can't keep tapping for score.
            hittable = false;
          }
          break;
        case MoleType.Bomb:
          // Game over, 1 for bomb.
          gameManager.GameOver(1);
          break;
        default:
          break;
      }
    }
  }

  private void CreateNext() {
    float random = Random.Range(0f, 1f);
    if (random < bombRate) {
      // Make a bomb.
      moleType = MoleType.Bomb;
      // The animator handles setting the sprite.
      animator.enabled = true;
    } else {
      animator.enabled = false;
      random = Random.Range(0f, 1f);
      if (random < hardRate) {
        // Create a hard one.
        moleType = MoleType.HardHat;
        spriteRenderer.sprite = moleHardHat;
        lives = 2;
      } else {
        // Create a standard one.
        moleType = MoleType.Standard;
        spriteRenderer.sprite = mole;
        lives = 1;
      }
    }
    // Mark as hittable so we can register an onclick event.
    hittable = true;
  }

  // As the level progresses the game gets harder.
  private void SetLevel(int level) {
    // As level increases increse the bomb rate to 0.25 at level 10.
    bombRate = Mathf.Min(level * 0.025f, 0.25f);

    // Increase the amounts of HardHats until 100% at level 40.
    hardRate = Mathf.Min(level * 0.025f, 1f);

    // Duration bounds get quicker as we progress. No cap on insanity.
    float durationMin = Mathf.Clamp(1 - level * 0.1f, 0.01f, 1f);
    float durationMax = Mathf.Clamp(2 - level * 0.1f, 0.01f, 2f);
    duration = Random.Range(durationMin, durationMax);
  }

  private void Awake() {
    // Get references to the components we'll need.
    spriteRenderer = GetComponent<SpriteRenderer>();
    animator = GetComponent<Animator>();
    boxCollider2D = GetComponent<BoxCollider2D>();
    // Work out collider values.
    boxOffset = boxCollider2D.offset; //position when fully shown i.e. (0,0)
    boxSize = boxCollider2D.size;
    boxOffsetHidden = new Vector2(boxOffset.x, -startPosition.y / 2f);
    boxSizeHidden = new Vector2(boxSize.x, 0f); //zero size at start

    // Build the procedural explosion sprites here (once per mole, Awake time).
    // We intentionally do NOT use Texture2D/asset references from static fields, since Unity
    // can unload those between assembly reloads / transient domains (confirmed via Unity
    // diagnostics: Flash/Smoke/Spark SpriteRenderers ended up with a null sprite ref).
    _procSmokeSprite = MakeSoftCircleSprite(96, Color.white);
    _procFlashSprite = MakeSoftCircleSprite(96, new Color(1f, 1f, 0.65f));
    _procEmberSprite = MakeSoftCircleSprite(20, new Color(1f, 0.9f, 0.25f));
    // Protect the underlying textures from being garbage-collected / unloaded mid-game.
    if (_procSmokeSprite != null) _procSmokeSprite.texture.hideFlags = HideFlags.DontSave;
    if (_procFlashSprite != null) _procFlashSprite.texture.hideFlags = HideFlags.DontSave;
    if (_procEmberSprite != null) _procEmberSprite.texture.hideFlags = HideFlags.DontSave;
  }

  public void Activate(int level) {
    SetLevel(level);
    CreateNext();
    StartCoroutine(ShowHide(startPosition, endPosition));
  }

  // Used by the game manager to uniquely identify moles. 
  public void SetIndex(int index) {
    moleIndex = index;
  }

  // Used to freeze the game on finish.
  public void StopGame() {
    hittable = false;
    StopAllCoroutines();
  }
}
