#include <SFML/Graphics.hpp>
#include <SFML/Audio.hpp>
#include <SFML/Window/Event.hpp>
#include <array>
#include <iostream>
#include <string>
#include <vector>
#include <cmath>
#include <algorithm>
#include <cstdint>
#include <cstring>
#include <memory>
#include <optional>
#include <stdexcept>

#define EXPORT extern "C" __declspec(dllexport)

static sf::RenderWindow *g_window = nullptr;
static sf::View g_uiView;
static sf::Vector2u g_uiViewSize{};

static std::optional<sf::Font> g_font;
static std::optional<sf::Texture> g_bgTexture;
static std::optional<sf::Sprite> g_bgSprite; 
static std::optional<sf::Texture> g_charTexture;
static std::optional<sf::Sprite> g_charSprite;

static std::string g_dialogText = "";
static sf::String g_dialogUnicode;
static float g_revealedCharacters = 0.0f;
static float g_textRevealSpeed = 42.0f;
static std::string g_characterName = "";
static std::vector<std::string> g_choices;
static std::vector<std::string> g_dialogueHistory;
static int g_fontSize = 16;
static int g_uiState = 0;
static int g_saveMenuMode = 0;
static bool g_currentSceneSeen = false;
static bool g_currentSceneIsChoice = false;
static float g_skipAdvanceTimer = 0.0f;
static bool g_skipAdvanceRequested = false;
static std::array<std::string, 6> g_saveSlotDescriptions;
static std::array<bool, 6> g_saveSlotExists{};

static float g_targetCharX = 0.0f;
static float g_targetCharY = 0.0f;
static bool g_hasCharacter = false;

static float g_shakeTimer = 0.0f;
static float g_shakeDuration = 0.0f;
static float g_shakeIntensity = 0.0f;

static float g_flashTimer = 0.0f;
static float g_flashDuration = 0.0f;
static sf::Color g_flashColor = sf::Color::White;

static std::string g_currentAnimType = "None";
static float g_animTimer = 0.0f;
static const float ANIM_DURATION = 0.4f;

// Аудио система
static std::unique_ptr<sf::Music> g_music;
static std::string g_lastError;

struct SoundInstance
{
    sf::SoundBuffer buffer;
    sf::Sound sound;

    explicit SoundInstance(const std::string &path) : sound(buffer)
    {
        if (!buffer.loadFromFile(path))
            throw std::runtime_error("Failed to load sound effect: " + path);
    }
};

static std::vector<std::unique_ptr<SoundInstance>> g_soundInstances;
static std::size_t g_nextSoundInstance = 0;
static constexpr std::size_t MAX_SOUND_INSTANCES = 16;
static float g_musicVolume = 100.0f;
static float g_fadeTimer = 0.0f;
static float g_fadeDuration = 0.0f;
static bool g_isFadingOut = false;

static void SetLastError(const std::string &message)
{
    if (!g_lastError.empty())
        g_lastError += '\n';
    g_lastError += message;
}

typedef void (*ChoiceCallbackFn)(int);
typedef void (*ClickCallbackFn)();
typedef void (*HotkeyCallbackFn)(int);
typedef void (*UiActionCallbackFn)(int);
static ChoiceCallbackFn g_choiceCallback = nullptr;
static ClickCallbackFn g_clickCallback = nullptr;
static HotkeyCallbackFn g_hotkeyCallback = nullptr;
static UiActionCallbackFn g_uiActionCallback = nullptr;

struct UiRect
{
    float left;
    float top;
    float width;
    float height;

    bool contains(float x, float y) const
    {
        return x >= left && x <= left + width && y >= top && y <= top + height;
    }
};

struct ChoiceLayout
{
    UiRect bounds;
    std::vector<sf::String> lines;
};

static constexpr int UI_HIDE_DIALOGUE = 1;
static constexpr int UI_AUTO_MODE = 2;
static constexpr int UI_SKIP_MODE = 4;
static constexpr int UI_HISTORY_OPEN = 8;

static UiRect GetSaveSlotPanel(float width, float height)
{
    const float panelWidth = std::min(760.0f, width - 48.0f);
    const float panelHeight = std::min(456.0f, height - 48.0f);
    return {(width - panelWidth) * 0.5f, (height - panelHeight) * 0.5f,
            panelWidth, panelHeight};
}

static UiRect GetSaveSlotBounds(const UiRect &panel, std::size_t slotIndex)
{
    const float gap = 12.0f;
    const float left = panel.left + 24.0f + (slotIndex % 2) *
        ((panel.width - 48.0f + gap) * 0.5f);
    const float top = panel.top + 96.0f + (slotIndex / 2) * 94.0f;
    const float buttonWidth = (panel.width - 60.0f) * 0.5f;
    return {left, top, buttonWidth, 80.0f};
}

static float MeasureText(const sf::Font &font, const sf::String &string, unsigned int size)
{
    sf::Text text(font, string, size);
    return text.getLocalBounds().size.x;
}

static std::vector<sf::String> WrapText(const sf::String &text, const sf::Font &font,
                                       unsigned int characterSize, float maxWidth,
                                       std::size_t maxLines)
{
    std::vector<sf::String> lines;
    if (maxLines == 0) return lines;

    sf::String line;
    sf::String word;
    bool truncated = false;
    const auto flushWord = [&]() {
        if (word.isEmpty()) return;

        sf::String candidate = line;
        if (!candidate.isEmpty()) candidate += U' ';
        candidate += word;

        if (MeasureText(font, candidate, characterSize) <= maxWidth || line.isEmpty()) {
            if (MeasureText(font, candidate, characterSize) <= maxWidth) {
                line = candidate;
                word.clear();
                return;
            }
        } else {
            lines.push_back(line);
            line.clear();
            if (lines.size() >= maxLines) {
                truncated = true;
                word.clear();
                return;
            }
        }

        for (std::size_t i = 0; i < word.getSize(); ++i) {
            sf::String fragment = line;
            fragment += word[i];
            if (!line.isEmpty() && MeasureText(font, fragment, characterSize) > maxWidth) {
                lines.push_back(line);
                line.clear();
                if (lines.size() >= maxLines) {
                    truncated = true;
                    break;
                }
            }
            line += word[i];
        }
        word.clear();
    };

    for (std::size_t i = 0; i < text.getSize(); ++i) {
        const char32_t character = text[i];
        if (character == U'\n' || character == U'\r') {
            flushWord();
            if (!truncated) {
                lines.push_back(line);
                line.clear();
                if (lines.size() >= maxLines && i + 1 < text.getSize()) truncated = true;
            }
        } else if (character == U' ' || character == U'\t') {
            flushWord();
        } else {
            word += character;
        }
        if (truncated) break;
    }
    if (!truncated) {
        flushWord();
        if (!line.isEmpty() || lines.empty()) lines.push_back(line);
        if (lines.size() > maxLines) {
            lines.resize(maxLines);
            truncated = true;
        }
    }

    if (truncated && !lines.empty()) {
        sf::String &lastLine = lines.back();
        const sf::String ellipsis(U"\u2026");
        while (!lastLine.isEmpty() &&
               MeasureText(font, lastLine + ellipsis, characterSize) > maxWidth) {
            lastLine.erase(lastLine.getSize() - 1);
        }
        lastLine += ellipsis;
    }
    return lines;
}

static UiRect GetDialogueBounds(float width, float height)
{
    const float margin = std::min(24.0f, width * 0.03f);
    const float panelHeight = std::min(220.0f, std::max(164.0f, height * 0.32f));
    return {margin, height - panelHeight - margin, width - margin * 2.0f, panelHeight};
}

static std::vector<UiRect> GetToolbarButtons(float width)
{
    constexpr float buttonWidth = 86.0f;
    constexpr float buttonGap = 8.0f;
    constexpr float buttonHeight = 36.0f;
    constexpr float rightMargin = 24.0f;
    constexpr std::size_t buttonCount = 6;
    const float totalWidth = static_cast<float>(buttonCount) * buttonWidth +
        static_cast<float>(buttonCount - 1) * buttonGap;
    const float left = std::max(16.0f, width - rightMargin - totalWidth);

    std::vector<UiRect> buttons;
    buttons.reserve(buttonCount);
    for (std::size_t index = 0; index < buttonCount; ++index)
        buttons.push_back({left + index * (buttonWidth + buttonGap), 18.0f,
                           buttonWidth, buttonHeight});
    return buttons;
}

static void DrawLabel(sf::RenderWindow &window, const std::string &label,
                      float x, float y, unsigned int size, sf::Color color)
{
    if (!g_font)
        return;

    sf::Text text(*g_font, sf::String::fromUtf8(label.begin(), label.end()), size);
    text.setPosition({x, y});
    text.setFillColor(color);
    window.draw(text);
}

static void DrawButton(sf::RenderWindow &window, const UiRect &bounds,
                       const std::string &label, bool hovered, bool active)
{
    sf::RectangleShape button({bounds.width, bounds.height});
    button.setPosition({bounds.left, bounds.top});
    button.setFillColor(active ? sf::Color(38, 77, 79, 245)
                               : hovered ? sf::Color(43, 56, 74, 245)
                                         : sf::Color(19, 27, 42, 228));
    button.setOutlineColor(active ? sf::Color(105, 218, 197, 220)
                                  : hovered ? sf::Color(115, 151, 174, 215)
                                            : sf::Color(92, 112, 137, 175));
    button.setOutlineThickness(1.0f);
    window.draw(button);

    const sf::String text = sf::String::fromUtf8(label.begin(), label.end());
    const float textWidth = g_font ? MeasureText(*g_font, text, 11) : 0.0f;
    DrawLabel(window, label, bounds.left + (bounds.width - textWidth) * 0.5f,
              bounds.top + 11.0f, 11,
              active || hovered ? sf::Color(239, 248, 246) : sf::Color(198, 209, 222));
}

static std::vector<ChoiceLayout> GetChoiceLayouts(float width, const UiRect &dialogueBounds)
{
    std::vector<ChoiceLayout> layouts;
    if (g_choices.empty()) return layouts;

    const float gap = 8.0f;
    const float top = 20.0f;
    const float availableHeight = std::max(40.0f, dialogueBounds.top - top - 16.0f);
    const float rowLimit = std::max(34.0f,
        (availableHeight - gap * static_cast<float>(g_choices.size() - 1)) /
        static_cast<float>(g_choices.size()));
    const float buttonWidth = std::min(680.0f, std::max(180.0f, width - 64.0f));
    const float buttonX = (width - buttonWidth) * 0.5f;
    const unsigned int textSize = static_cast<unsigned int>(std::clamp(g_fontSize - 1, 11, 20));
    const float lineHeight = std::max(15.0f, static_cast<float>(textSize) * 1.3f);
    const std::size_t maxLines = static_cast<std::size_t>(
        std::max(1.0f, std::floor((rowLimit - 18.0f) / lineHeight)));
    float y = top;

    for (const auto &choice : g_choices) {
        ChoiceLayout layout;
        if (g_font) {
            const sf::String text = sf::String::fromUtf8(choice.begin(), choice.end());
            layout.lines = WrapText(text, *g_font, textSize, buttonWidth - 52.0f, maxLines);
        }
        const float contentHeight = std::max(1.0f, static_cast<float>(layout.lines.size())) * lineHeight;
        const float buttonHeight = std::min(rowLimit, std::max(42.0f, contentHeight + 18.0f));
        layout.bounds = {buttonX, y, buttonWidth, buttonHeight};
        layouts.push_back(layout);
        y += buttonHeight + gap;
    }
    return layouts;
}

static sf::Color HexToColor(const char *hex)
{
    if (!hex) return sf::Color::White;
    std::string s(hex);
    if (!s.empty() && s[0] == '#') s.erase(0, 1);
    if (s.length() < 6) return sf::Color::White;

    try {
        unsigned int r = std::stoul(s.substr(0, 2), nullptr, 16);
        unsigned int g = std::stoul(s.substr(2, 2), nullptr, 16);
        unsigned int b = std::stoul(s.substr(4, 2), nullptr, 16);
        return sf::Color(static_cast<std::uint8_t>(r), 
                         static_cast<std::uint8_t>(g), 
                         static_cast<std::uint8_t>(b), 255);
    } catch (...) {
        return sf::Color::White;
    }
}

static void UpdateVFX(float dt)
{
    if (g_shakeTimer > 0.0f) {
        g_shakeTimer -= dt;
        if (g_shakeTimer < 0.0f) g_shakeTimer = 0.0f;
    }
    if (g_flashTimer > 0.0f) {
        g_flashTimer -= dt;
        if (g_flashTimer < 0.0f) g_flashTimer = 0.0f;
    }
    if (g_animTimer < ANIM_DURATION) {
        g_animTimer += dt;
        if (g_animTimer > ANIM_DURATION) g_animTimer = ANIM_DURATION;
    }

    // Обработка плавного угасания музыки
    if (g_isFadingOut && g_fadeTimer > 0.0f) {
        g_fadeTimer -= dt;
        if (g_fadeTimer <= 0.0f) {
            if (g_music)
                g_music->stop();
            g_isFadingOut = false;
        } else if (g_music) {
            float currentVol = g_musicVolume * (g_fadeTimer / g_fadeDuration);
            g_music->setVolume(currentVol);
        }
    }
}

static void RenderCharacter(sf::RenderWindow &window, float targetX, float targetY)
{
    if (!g_hasCharacter || !g_charSprite) return;

    float progress = (ANIM_DURATION > 0.0f) ? (g_animTimer / ANIM_DURATION) : 1.0f;
    progress = std::clamp(progress, 0.0f, 1.0f);

    float currentX = targetX;
    float currentY = targetY;
    std::uint8_t alpha = 255;

    if (g_currentAnimType == "FadeIn") {
        alpha = static_cast<std::uint8_t>(255 * progress);
    } else if (g_currentAnimType == "SlideLeft") {
        float offset = (1.0f - progress) * 150.0f;
        currentX = targetX + offset;
        alpha = static_cast<std::uint8_t>(255 * progress);
    } else if (g_currentAnimType == "SlideRight") {
        float offset = (1.0f - progress) * 150.0f;
        currentX = targetX - offset;
        alpha = static_cast<std::uint8_t>(255 * progress);
    } else if (g_currentAnimType == "Bounce") {
        float bounceOffset = std::sin(progress * 3.14159f) * 30.0f;
        currentY = targetY - bounceOffset;
    }

    g_charSprite->setPosition({currentX, currentY});
    g_charSprite->setColor(sf::Color(255, 255, 255, alpha));
    window.draw(*g_charSprite);
}

EXPORT void InitEngine(int width, int height, const char *title, const char *basePath)
{
    g_lastError.clear();
    if (g_window) {
        delete g_window;
        g_window = nullptr;
    }
        
    sf::VideoMode mode({static_cast<unsigned int>(width), static_cast<unsigned int>(height)});
    g_window = new sf::RenderWindow(mode, title ? title : "MyGameNovella Engine");
    g_window->setVisible(true);
    if (!g_window->isOpen())
        SetLastError("SFML could not create the game window.");
    g_window->setFramerateLimit(60);
    g_uiView = g_window->getDefaultView();
    g_uiViewSize = g_window->getSize();
    const std::string fontPath = std::string(basePath ? basePath : "") + "arial.ttf";
    g_font.emplace();
    if (!g_font->openFromFile(fontPath)) {
        const char *fallbackFonts[] = {
            "C:/Windows/Fonts/arial.ttf",
            "C:/Windows/Fonts/segoeui.ttf"
        };
        bool loadedFallback = false;
        for (const char *fallbackPath : fallbackFonts) {
            if (g_font->openFromFile(fallbackPath)) {
                std::cerr << "[C++ Engine] Warning: " << fontPath
                          << " unavailable; using " << fallbackPath << std::endl;
                loadedFallback = true;
                break;
            }
        }
        if (!loadedFallback) {
            std::cerr << "[C++ Engine] Error: Failed to load font " << fontPath
                      << " or Windows fallback fonts; dialogue and choices cannot be rendered."
                      << std::endl;
            g_font.reset();
            SetLastError("Failed to load the game font from the package or Windows fallback fonts.");
        }
    }
}

EXPORT void ShutdownEngine()
{
    if (g_music) {
        g_music->stop();
        g_music.reset();
    }
    for (const auto &instance : g_soundInstances)
        instance->sound.stop();
    g_soundInstances.clear();
    g_nextSoundInstance = 0;
    if (g_window) {
        g_window->close();
        delete g_window;
        g_window = nullptr;
    }
}

EXPORT void UpdateScene(const char *bgPath, const char *text, const char *charName, const char *charSpritePath, float charX, float charY)
{
    if (bgPath && std::string(bgPath).length() > 0) {
        g_bgTexture.emplace();
        if (g_bgTexture->loadFromFile(bgPath)) {
            g_bgSprite.emplace(*g_bgTexture); 
        } else {
            g_bgSprite.reset();
            SetLastError(std::string("Failed to load background image: ") + bgPath);
        }
    } else {
        g_bgSprite.reset();
        g_bgTexture.reset();
    }

    g_dialogText = text ? text : "";
    g_dialogUnicode = sf::String::fromUtf8(g_dialogText.begin(), g_dialogText.end());
    g_revealedCharacters = g_textRevealSpeed <= 0.0f
        ? static_cast<float>(g_dialogUnicode.getSize())
        : 0.0f;
    g_characterName = charName ? charName : "";
    g_skipAdvanceTimer = 0.0f;
    g_skipAdvanceRequested = false;

    if (charSpritePath && std::string(charSpritePath).length() > 0) {
        g_charTexture.emplace();
        if (g_charTexture->loadFromFile(charSpritePath)) {
            g_charSprite.emplace(*g_charTexture);
            g_targetCharX = charX;
            g_targetCharY = charY;
            g_hasCharacter = true;
        } else {
            g_hasCharacter = false;
            g_charSprite.reset();
            SetLastError(std::string("Failed to load character sprite: ") + charSpritePath);
        }
    } else {
        g_hasCharacter = false;
        g_charSprite.reset(); 
    }
}

EXPORT void SetPlaybackState(bool sceneSeen, bool isChoice)
{
    g_currentSceneSeen = sceneSeen;
    g_currentSceneIsChoice = isChoice;
    g_skipAdvanceTimer = 0.0f;
    g_skipAdvanceRequested = false;
}

EXPORT void SetGameUiState(int state)
{
    g_uiState = state;
    if ((g_uiState & UI_SKIP_MODE) == 0)
        g_skipAdvanceRequested = false;
}

EXPORT void SetSaveMenuState(int mode)
{
    g_saveMenuMode = std::clamp(mode, 0, 2);
    if (g_saveMenuMode != 0)
        g_uiState &= ~UI_HISTORY_OPEN;
}

EXPORT void SetSaveSlotInfo(int slotIndex, const char *description, bool exists)
{
    if (slotIndex < 0 || slotIndex >= static_cast<int>(g_saveSlotDescriptions.size()))
        return;
    g_saveSlotDescriptions[static_cast<std::size_t>(slotIndex)] =
        description ? description : "";
    g_saveSlotExists[static_cast<std::size_t>(slotIndex)] = exists;
}

EXPORT void RegisterUiActionCallback(UiActionCallbackFn callback)
{
    g_uiActionCallback = callback;
}

EXPORT void AddDialogueHistory(const char *speaker, const char *text)
{
    const std::string entry = std::string(speaker ? speaker : "") + '\n' +
        (text ? text : "");
    g_dialogueHistory.push_back(entry);
    constexpr std::size_t MAX_HISTORY_ENTRIES = 250;
    if (g_dialogueHistory.size() > MAX_HISTORY_ENTRIES)
        g_dialogueHistory.erase(g_dialogueHistory.begin());
}

EXPORT void UpdateChoices(const char *c1, const char *c2, const char *c3, const char *c4)
{
    g_choices.clear();
    if (c1 && std::string(c1).length() > 0) g_choices.push_back(c1);
    if (c2 && std::string(c2).length() > 0) g_choices.push_back(c2);
    if (c3 && std::string(c3).length() > 0) g_choices.push_back(c3);
    if (c4 && std::string(c4).length() > 0) g_choices.push_back(c4);
}

EXPORT void ShakeScreen(float duration, float intensity)
{
    g_shakeDuration = duration;
    g_shakeTimer = duration;
    g_shakeIntensity = intensity;
}

EXPORT void FlashScreen(const char *hexColor, float duration)
{
    g_flashColor = HexToColor(hexColor);
    g_flashDuration = duration;
    g_flashTimer = duration;
}

EXPORT void SetCharacterAnimation(const char *animType)
{
    g_currentAnimType = animType ? animType : "None";
    g_animTimer = 0.0f; 
}

EXPORT void RegisterChoiceCallback(ChoiceCallbackFn callback) { g_choiceCallback = callback; }
EXPORT void RegisterClickCallback(ClickCallbackFn callback) { g_clickCallback = callback; }
EXPORT void RegisterHotkeyCallback(HotkeyCallbackFn callback) { g_hotkeyCallback = callback; }

EXPORT void SetFontSize(int size)
{
    if (size > 0) g_fontSize = size;
}

EXPORT void SetTextRevealSpeed(float charactersPerSecond)
{
    if (!std::isfinite(charactersPerSecond) || charactersPerSecond < 0.0f) {
        SetLastError("Text reveal speed must be a finite non-negative number.");
        return;
    }
    g_textRevealSpeed = charactersPerSecond;
    if (g_textRevealSpeed == 0.0f)
        g_revealedCharacters = static_cast<float>(g_dialogUnicode.getSize());
}

EXPORT void PlayMusic(const char* trackName)
{
    if (!trackName || std::string(trackName).empty()) return;

    g_isFadingOut = false;
    try {
        if (!g_music)
            g_music = std::make_unique<sf::Music>();
        g_music->stop();

        if (g_music->openFromFile(trackName)) {
            g_music->setLooping(true);
            g_music->setVolume(g_musicVolume);
            g_music->play();
            std::cout << "[C++ Engine] Играет трек: " << trackName << std::endl;
        } else {
            std::cerr << "[C++ Engine] Ошибка: Не удалось загрузить аудиофайл " << trackName << std::endl;
            SetLastError(std::string("Failed to load background music: ") + trackName);
        }
    } catch (const std::exception &exception) {
        SetLastError(exception.what());
    }
}

EXPORT void PlaySound(const char *soundPath)
{
    if (!soundPath || std::string(soundPath).empty())
        return;

    try {
        std::size_t slot = g_soundInstances.size();
        for (std::size_t index = 0; index < g_soundInstances.size(); ++index) {
            if (g_soundInstances[index]->sound.getStatus() == sf::SoundSource::Status::Stopped) {
                slot = index;
                break;
            }
        }

        if (slot == g_soundInstances.size() && g_soundInstances.size() < MAX_SOUND_INSTANCES) {
            g_soundInstances.push_back(std::make_unique<SoundInstance>(soundPath));
        } else {
            if (slot == g_soundInstances.size()) {
                slot = g_nextSoundInstance % g_soundInstances.size();
                ++g_nextSoundInstance;
            }
            auto replacement = std::make_unique<SoundInstance>(soundPath);
            g_soundInstances[slot]->sound.stop();
            g_soundInstances[slot] = std::move(replacement);
        }

        g_soundInstances[slot]->sound.play();
    } catch (const std::exception &exception) {
        SetLastError(exception.what());
    }
}

EXPORT void StopSounds()
{
    for (const auto &instance : g_soundInstances)
        instance->sound.stop();
}

EXPORT int GetLastErrorMessage(char *buffer, int capacity)
{
    if (g_lastError.empty() || !buffer || capacity <= 0)
        return 0;

    const int messageLength = static_cast<int>(g_lastError.size());
    const int bytesToCopy = std::min(messageLength, capacity - 1);
    std::memcpy(buffer, g_lastError.data(), static_cast<std::size_t>(bytesToCopy));
    buffer[bytesToCopy] = '\0';
    g_lastError.clear();
    return messageLength;
}

EXPORT void StopMusic()
{
    if (g_music)
        g_music->stop();
    g_isFadingOut = false;
}

EXPORT void SetMusicVolume(float volume)
{
    g_musicVolume = std::clamp(volume, 0.0f, 100.0f);
    if (g_music && !g_isFadingOut)
        g_music->setVolume(g_musicVolume);
}

EXPORT void StartMusicFadeOut(float durationSeconds)
{
    if (!g_music)
        return;
    if (durationSeconds <= 0.0f) {
        StopMusic();
        return;
    }
    g_fadeDuration = durationSeconds;
    g_fadeTimer = durationSeconds;
    g_isFadingOut = true;
}

EXPORT bool TickEngine(float deltaTime)
{
    if (!g_window || !g_window->isOpen()) return false;
    const bool skipMode = (g_uiState & UI_SKIP_MODE) != 0 && g_currentSceneSeen;
    const bool autoMode = (g_uiState & UI_AUTO_MODE) != 0;
    const float revealSpeed = skipMode ? 10000.0f : g_textRevealSpeed;
    if (revealSpeed > 0.0f && g_revealedCharacters < static_cast<float>(g_dialogUnicode.getSize()))
        g_revealedCharacters = std::min(
            static_cast<float>(g_dialogUnicode.getSize()),
            g_revealedCharacters + revealSpeed * std::max(0.0f, deltaTime));

    const sf::Vector2u windowSize = g_window->getSize();
    if (windowSize.x == 0 || windowSize.y == 0)
        return true;
    const float width = static_cast<float>(windowSize.x);
    const float height = static_cast<float>(windowSize.y);
    if (windowSize.x != g_uiViewSize.x || windowSize.y != g_uiViewSize.y) {
        g_uiView.setSize({width, height});
        g_uiView.setCenter({width * 0.5f, height * 0.5f});
        g_uiViewSize = windowSize;
    }
    const bool dialogueHidden = (g_uiState & UI_HIDE_DIALOGUE) != 0;
    const bool historyOpen = (g_uiState & UI_HISTORY_OPEN) != 0;
    const UiRect dialogueBounds = dialogueHidden
        ? UiRect{0.0f, height, width, 0.0f}
        : GetDialogueBounds(width, height);
    const std::vector<ChoiceLayout> choiceLayouts = GetChoiceLayouts(width, dialogueBounds);
    const std::vector<UiRect> toolbarButtons = GetToolbarButtons(width);

    while (const std::optional<sf::Event> event = g_window->pollEvent())
    {
        if (event->is<sf::Event::Closed>()) {
            g_window->close();
            return false;
        }

        if (const auto *keyEvent = event->getIf<sf::Event::KeyPressed>()) {
            if (keyEvent->code == sf::Keyboard::Key::F5 && g_hotkeyCallback)
                g_hotkeyCallback(100);
            else if (keyEvent->code == sf::Keyboard::Key::F9 && g_hotkeyCallback)
                g_hotkeyCallback(200);
            else if (keyEvent->code == sf::Keyboard::Key::H && g_uiActionCallback)
                g_uiActionCallback(4);
            else if (keyEvent->code == sf::Keyboard::Key::A && g_uiActionCallback)
                g_uiActionCallback(3);
            else if (keyEvent->code == sf::Keyboard::Key::T && g_uiActionCallback)
                g_uiActionCallback(5);
            else if (keyEvent->code == sf::Keyboard::Key::L && g_uiActionCallback)
                g_uiActionCallback(2);
            else if (keyEvent->code == sf::Keyboard::Key::Escape && historyOpen && g_uiActionCallback)
                g_uiActionCallback(2);
            else if (keyEvent->code == sf::Keyboard::Key::Escape &&
                     g_saveMenuMode != 0 && g_uiActionCallback)
                g_uiActionCallback(8);
            else if (keyEvent->code == sf::Keyboard::Key::Space && g_uiActionCallback)
                g_uiActionCallback(6);
        } else if (const auto *keyEvent = event->getIf<sf::Event::KeyReleased>()) {
            if (keyEvent->code == sf::Keyboard::Key::Space && g_uiActionCallback)
                g_uiActionCallback(7);
        }

        if (event->is<sf::Event::MouseButtonPressed>()) {
            auto mouseEvent = event->getIf<sf::Event::MouseButtonPressed>();
            if (mouseEvent && mouseEvent->button == sf::Mouse::Button::Left) {
                float mouseX = static_cast<float>(mouseEvent->position.x);
                float mouseY = static_cast<float>(mouseEvent->position.y);

                bool clickedToolbar = false;
                for (std::size_t index = 0; index < toolbarButtons.size(); ++index) {
                    if (!toolbarButtons[index].contains(mouseX, mouseY))
                        continue;
                    if (g_uiActionCallback)
                        g_uiActionCallback(static_cast<int>(index));
                    clickedToolbar = true;
                    break;
                }
                if (clickedToolbar)
                    continue;

                if (g_saveMenuMode != 0) {
                    const UiRect panel = GetSaveSlotPanel(width, height);
                    const UiRect closeButton{panel.left + panel.width - 104.0f,
                                             panel.top + 24.0f, 80.0f, 34.0f};
                    if (!panel.contains(mouseX, mouseY) || closeButton.contains(mouseX, mouseY)) {
                        if (g_uiActionCallback)
                            g_uiActionCallback(8);
                        continue;
                    }

                    for (std::size_t slot = 0; slot < g_saveSlotDescriptions.size(); ++slot) {
                        if (GetSaveSlotBounds(panel, slot).contains(mouseX, mouseY)) {
                            const int action = (g_saveMenuMode == 1 ? 100 : 200) +
                                static_cast<int>(slot);
                            if (g_uiActionCallback)
                                g_uiActionCallback(action);
                            break;
                        }
                    }
                    continue;
                }

                if (historyOpen) {
                    if (g_uiActionCallback)
                        g_uiActionCallback(2);
                    continue;
                }

                if (dialogueHidden)
                    continue;

                bool clickedChoice = false;

                for (size_t i = 0; !dialogueHidden && i < choiceLayouts.size(); ++i) {
                    if (choiceLayouts[i].bounds.contains(mouseX, mouseY)) {
                        if (g_choiceCallback) {
                            g_choiceCallback(static_cast<int>(i));
                        }
                        clickedChoice = true;
                        break;
                    }
                }

                if (!clickedChoice) {
                    if (g_revealedCharacters < static_cast<float>(g_dialogUnicode.getSize())) {
                        g_revealedCharacters = static_cast<float>(g_dialogUnicode.getSize());
                    } else if (g_clickCallback) {
                        g_clickCallback();
                        g_skipAdvanceRequested = false;
                        g_skipAdvanceTimer = 0.0f;
                    }
                }
            }
        }
    }

    UpdateVFX(deltaTime);

    if (((skipMode && !g_currentSceneIsChoice) || autoMode) &&
        g_revealedCharacters >= static_cast<float>(g_dialogUnicode.getSize()) &&
        !g_skipAdvanceRequested && g_clickCallback) {
        g_skipAdvanceTimer += std::max(0.0f, deltaTime);
        if (g_skipAdvanceTimer >= (autoMode ? 1.1f : 0.22f)) {
            g_skipAdvanceRequested = true;
            g_clickCallback();
        }
    } else if (!skipMode && !autoMode) {
        g_skipAdvanceTimer = 0.0f;
    }

    sf::View activeView = g_uiView;

    if (g_shakeTimer > 0.0f) {
        float offsetX = ((rand() % 100) / 100.0f - 0.5f) * 2.0f * g_shakeIntensity;
        float offsetY = ((rand() % 100) / 100.0f - 0.5f) * 2.0f * g_shakeIntensity;
        float factor = g_shakeTimer / g_shakeDuration;
        activeView.move({offsetX * factor, offsetY * factor}); 
    }
    g_window->setView(activeView);

    g_window->clear(sf::Color(12, 17, 30));

    if (g_bgSprite) g_window->draw(*g_bgSprite);
    RenderCharacter(*g_window, g_targetCharX, g_targetCharY);

    // Keep interface geometry and pointer hit-testing stable while the scene shakes.
    g_window->setView(g_uiView);

    const sf::Vector2i mousePosition = sf::Mouse::getPosition(*g_window);
    const float mouseX = static_cast<float>(mousePosition.x);
    const float mouseY = static_cast<float>(mousePosition.y);

    const std::array<std::string, 6> buttonLabels = {
        "SAVE", "LOAD", "HISTORY", "AUTO", "HIDE UI", "SKIP"
    };
    for (std::size_t index = 0; index < toolbarButtons.size(); ++index) {
        const bool hovered = toolbarButtons[index].contains(mouseX, mouseY);
        const bool active = (index == 3 && autoMode) ||
                            (index == 4 && dialogueHidden) ||
                            (index == 5 && (g_uiState & UI_SKIP_MODE) != 0);
        DrawButton(*g_window, toolbarButtons[index], buttonLabels[index], hovered, active);
    }

    if (!dialogueHidden) {
        const sf::Color panelFill(10, 15, 27, 232);
        const sf::Color accent(93, 213, 204);
        sf::RectangleShape dialoguePanel({dialogueBounds.width, dialogueBounds.height});
        dialoguePanel.setPosition({dialogueBounds.left, dialogueBounds.top});
        dialoguePanel.setFillColor(panelFill);
        dialoguePanel.setOutlineColor(sf::Color(133, 165, 179, 195));
        dialoguePanel.setOutlineThickness(1.0f);
        g_window->draw(dialoguePanel);

        sf::RectangleShape panelAccent({dialogueBounds.width, 3.0f});
        panelAccent.setPosition({dialogueBounds.left, dialogueBounds.top});
        panelAccent.setFillColor(accent);
        g_window->draw(panelAccent);

        if (g_font) {
            const unsigned int dialogueSize = static_cast<unsigned int>(std::clamp(g_fontSize, 12, 25));
            const unsigned int nameSize = static_cast<unsigned int>(std::clamp(g_fontSize + 4, 14, 29));
            const float contentLeft = dialogueBounds.left + 28.0f;
            const float contentWidth = dialogueBounds.width - 56.0f;
            const sf::String name = sf::String::fromUtf8(g_characterName.begin(), g_characterName.end());
            float textTop = dialogueBounds.top + 23.0f;

            if (!g_characterName.empty()) {
                sf::Text nameText(*g_font, name, nameSize);
                nameText.setPosition({contentLeft, textTop});
                nameText.setFillColor(sf::Color(246, 207, 128));
                g_window->draw(nameText);
                textTop += static_cast<float>(nameSize) * 1.65f;
            }

            sf::String dialogue;
            const std::size_t visibleCharacters = std::min(
                g_dialogUnicode.getSize(),
                static_cast<std::size_t>(std::max(0.0f, g_revealedCharacters)));
            for (std::size_t index = 0; index < visibleCharacters; ++index)
                dialogue += g_dialogUnicode[index];
            const float lineHeight = std::max(17.0f, static_cast<float>(dialogueSize) * 1.4f);
            const std::size_t maxDialogueLines = static_cast<std::size_t>(
                std::max(1.0f, std::floor((dialogueBounds.top + dialogueBounds.height -
                                          textTop - 24.0f) / lineHeight)));
            const auto dialogueLines = WrapText(dialogue, *g_font, dialogueSize,
                                                contentWidth, maxDialogueLines);
            for (std::size_t i = 0; i < dialogueLines.size(); ++i) {
                sf::Text line(*g_font, dialogueLines[i], dialogueSize);
                line.setPosition({contentLeft, textTop + static_cast<float>(i) * lineHeight});
                line.setFillColor(sf::Color(238, 242, 248));
                g_window->draw(line);
            }

            const float progress = g_dialogUnicode.getSize() == 0 ? 1.0f :
                std::clamp(g_revealedCharacters / static_cast<float>(g_dialogUnicode.getSize()), 0.0f, 1.0f);
            const float progressWidth = (dialogueBounds.width - 2.0f) * progress;
            sf::RectangleShape progressTrack({dialogueBounds.width - 2.0f, 2.0f});
            progressTrack.setPosition({dialogueBounds.left + 1.0f,
                                       dialogueBounds.top + dialogueBounds.height - 3.0f});
            progressTrack.setFillColor(sf::Color(49, 59, 72, 220));
            g_window->draw(progressTrack);
            sf::RectangleShape progressFill({progressWidth, 2.0f});
            progressFill.setPosition({dialogueBounds.left + 1.0f,
                                      dialogueBounds.top + dialogueBounds.height - 3.0f});
            progressFill.setFillColor(sf::Color(93, 213, 204, 240));
            g_window->draw(progressFill);
        }
    }

    for (const auto &layout : choiceLayouts) {
        if (dialogueHidden || historyOpen)
            break;
        const bool hovered = layout.bounds.contains(mouseX, mouseY);
        sf::RectangleShape button({layout.bounds.width, layout.bounds.height});
        button.setPosition({layout.bounds.left, layout.bounds.top});
        button.setFillColor(hovered ? sf::Color(35, 67, 82, 245)
                                    : sf::Color(13, 20, 34, 238));
        button.setOutlineColor(hovered ? sf::Color(93, 213, 204)
                                       : sf::Color(111, 145, 169, 190));
        button.setOutlineThickness(hovered ? 2.0f : 1.0f);
        g_window->draw(button);

        sf::RectangleShape choiceAccent({3.0f, layout.bounds.height});
        choiceAccent.setPosition({layout.bounds.left, layout.bounds.top});
        choiceAccent.setFillColor(hovered ? sf::Color(246, 207, 128)
                                          : sf::Color(93, 213, 204));
        g_window->draw(choiceAccent);

        if (g_font) {
            const unsigned int choiceSize = static_cast<unsigned int>(
                std::clamp(g_fontSize - 1, 11, 20));
            const float lineHeight = std::max(15.0f, static_cast<float>(choiceSize) * 1.3f);
            const float textHeight = static_cast<float>(layout.lines.size()) * lineHeight;
            float lineY = layout.bounds.top + (layout.bounds.height - textHeight) * 0.5f;
            for (const auto &wrappedLine : layout.lines) {
                sf::Text choiceText(*g_font, wrappedLine, choiceSize);
                choiceText.setPosition({layout.bounds.left + 22.0f, lineY});
                choiceText.setFillColor(sf::Color(240, 245, 250));
                g_window->draw(choiceText);
                lineY += lineHeight;
            }
        }
    }

    if (historyOpen) {
        sf::RectangleShape backdrop({width, height});
        backdrop.setFillColor(sf::Color(4, 8, 16, 220));
        g_window->draw(backdrop);

        const float panelWidth = std::min(780.0f, width - 64.0f);
        const float panelHeight = std::min(height - 96.0f, 620.0f);
        const UiRect historyPanel{(width - panelWidth) * 0.5f,
                                  (height - panelHeight) * 0.5f,
                                  panelWidth, panelHeight};
        sf::RectangleShape panel({historyPanel.width, historyPanel.height});
        panel.setPosition({historyPanel.left, historyPanel.top});
        panel.setFillColor(sf::Color(13, 20, 34, 252));
        panel.setOutlineColor(sf::Color(102, 146, 158, 230));
        panel.setOutlineThickness(1.0f);
        g_window->draw(panel);
        DrawLabel(*g_window, "STORY HISTORY", historyPanel.left + 28.0f,
                  historyPanel.top + 22.0f, 22, sf::Color(239, 244, 250));
        DrawLabel(*g_window, "ESC  /  click outside to close",
                  historyPanel.left + 28.0f, historyPanel.top + 56.0f, 12,
                  sf::Color(145, 161, 178));

        if (g_font && panelHeight > 130.0f) {
            float y = historyPanel.top + 94.0f;
            const float maxY = historyPanel.top + historyPanel.height - 24.0f;
            std::size_t shown = 0;
            for (auto iterator = g_dialogueHistory.rbegin();
                 iterator != g_dialogueHistory.rend() && y + 38.0f < maxY &&
                 shown < 12; ++iterator, ++shown) {
                const std::size_t separator = iterator->find('\n');
                const std::string speaker = iterator->substr(0, separator);
                const std::string line = separator == std::string::npos
                    ? std::string{} : iterator->substr(separator + 1);
                DrawLabel(*g_window, speaker.empty() ? "NARRATION" : speaker,
                          historyPanel.left + 28.0f, y, 13,
                          sf::Color(246, 207, 128));
                y += 21.0f;
                const sf::String historyText = sf::String::fromUtf8(line.begin(), line.end());
                const auto lines = WrapText(historyText, *g_font, 14,
                                            historyPanel.width - 56.0f, 2);
                for (const auto &wrappedLine : lines) {
                    sf::Text entry(*g_font, wrappedLine, 14);
                    entry.setPosition({historyPanel.left + 28.0f, y});
                    entry.setFillColor(sf::Color(220, 228, 238));
                    g_window->draw(entry);
                    y += 20.0f;
                }
                y += 12.0f;
            }
        }
    }

    if (g_saveMenuMode != 0) {
        sf::RectangleShape backdrop({width, height});
        backdrop.setFillColor(sf::Color(4, 8, 16, 224));
        g_window->draw(backdrop);

        const UiRect panelBounds = GetSaveSlotPanel(width, height);
        sf::RectangleShape panel({panelBounds.width, panelBounds.height});
        panel.setPosition({panelBounds.left, panelBounds.top});
        panel.setFillColor(sf::Color(13, 20, 34, 252));
        panel.setOutlineColor(sf::Color(102, 146, 158, 235));
        panel.setOutlineThickness(1.0f);
        g_window->draw(panel);
        DrawLabel(*g_window, g_saveMenuMode == 1 ? "SAVE GAME" : "LOAD GAME",
                  panelBounds.left + 26.0f, panelBounds.top + 24.0f,
                  22, sf::Color(239, 244, 250));
        DrawLabel(*g_window, "Choose a slot  |  F5 quick save  |  F9 quick load  |  ESC close",
                  panelBounds.left + 26.0f, panelBounds.top + 58.0f,
                  12, sf::Color(145, 161, 178));
        DrawButton(*g_window,
                   {panelBounds.left + panelBounds.width - 104.0f,
                    panelBounds.top + 24.0f, 80.0f, 34.0f},
                   "CLOSE", false, false);

        for (std::size_t slot = 0; slot < g_saveSlotDescriptions.size(); ++slot) {
            const UiRect bounds = GetSaveSlotBounds(panelBounds, slot);
            const bool hovered = bounds.contains(mouseX, mouseY);
            DrawButton(*g_window, bounds, "", hovered, false);
            DrawLabel(*g_window, "SLOT " + std::to_string(slot + 1),
                      bounds.left + 14.0f, bounds.top + 12.0f,
                      13, sf::Color(110, 218, 198));
            const std::string fallback = g_saveMenuMode == 1
                ? "Empty slot - click to save"
                : "Empty slot";
            const std::string &slotDescription = g_saveSlotExists[slot]
                ? g_saveSlotDescriptions[slot] : fallback;
            const sf::String description = sf::String::fromUtf8(
                slotDescription.begin(), slotDescription.end());
            if (g_font) {
                const auto lines = WrapText(description, *g_font, 12,
                                            bounds.width - 28.0f, 2);
                for (std::size_t lineIndex = 0; lineIndex < lines.size(); ++lineIndex) {
                    sf::Text line(*g_font, lines[lineIndex], 12);
                    line.setPosition({bounds.left + 14.0f,
                                      bounds.top + 38.0f + lineIndex * 17.0f});
                    line.setFillColor(sf::Color(211, 221, 232));
                    g_window->draw(line);
                }
            }
        }
    }

    if (g_flashTimer > 0.0f) {
        sf::RectangleShape flashOverlay({static_cast<float>(g_window->getSize().x), 
                                         static_cast<float>(g_window->getSize().y)});
        float alphaFactor = g_flashTimer / g_flashDuration;
        sf::Color currentColor = g_flashColor;
        currentColor.a = static_cast<std::uint8_t>(255 * alphaFactor);
        flashOverlay.setFillColor(currentColor);

        g_window->setView(g_uiView);
        g_window->draw(flashOverlay);
    }

    g_window->display();
    return true;
}