#include <SFML/Graphics.hpp>
#include <SFML/Window/Event.hpp>
#include <iostream>
#include <string>
#include <vector>
#include <cmath>
#include <algorithm>
#include <cstdint>
#include <optional>

#define EXPORT extern "C" __declspec(dllexport)

// ============================================================================
// ГЛОБАЛЬНОЕ СОСТОЯНИЕ ДВИЖКА (БЕЗОПАСНАЯ ИНИЦИАЛИЗАЦИЯ)
// ============================================================================

static sf::RenderWindow *g_window = nullptr;

// ОБЪЕКТЫ ОБЕРНУТЫ В std::optional, ЧТОБЫ НЕ ВЫЗЫВАТЬ КОНСТРУКТОРЫ ПРИ ЗАГРУЗКЕ DLL
static std::optional<sf::Font> g_font;
static std::optional<sf::Texture> g_bgTexture;
static std::optional<sf::Sprite> g_bgSprite; 
static std::optional<sf::Texture> g_charTexture;
static std::optional<sf::Sprite> g_charSprite;

static std::string g_dialogText = "";
static std::string g_characterName = "";
static std::vector<std::string> g_choices;

static float g_targetCharX = 0.0f;
static float g_targetCharY = 0.0f;
static bool g_hasCharacter = false;

// VFX
static float g_shakeTimer = 0.0f;
static float g_shakeDuration = 0.0f;
static float g_shakeIntensity = 0.0f;

static float g_flashTimer = 0.0f;
static float g_flashDuration = 0.0f;
static sf::Color g_flashColor = sf::Color::White;

// Анимация
static std::string g_currentAnimType = "None";
static float g_animTimer = 0.0f;
static const float ANIM_DURATION = 0.4f;

// Коллбэки для C#
typedef void (*ChoiceCallbackFn)(int);
typedef void (*ClickCallbackFn)();
static ChoiceCallbackFn g_choiceCallback = nullptr;
static ClickCallbackFn g_clickCallback = nullptr;

// ============================================================================
// ВСПОМОГАТЕЛЬНЫЕ ФУНКЦИИ
// ============================================================================

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

// ============================================================================
// C-API (ФУНКЦИИ ДЛЯ ВЫЗОВА ИЗ C#)
// ============================================================================

EXPORT void InitEngine(int width, int height, const char *title, const char *basePath)
{
    if (g_window) {
        delete g_window;
        g_window = nullptr;
    }
        
    sf::VideoMode mode({static_cast<unsigned int>(width), static_cast<unsigned int>(height)});
    g_window = new sf::RenderWindow(mode, title ? title : "MyGameNovella Engine");
    g_window->setFramerateLimit(60);

    // Формируем абсолютный путь: basePath от C# уже содержит слеш на конце
    std::string fontPath = std::string(basePath) + "arial.ttf";

    g_font.emplace();
    if (!g_font->openFromFile(fontPath)) {
        std::cerr << "[C++ Engine] Error: Failed to load " << fontPath << std::endl;
        g_font.reset();
    }
}

EXPORT void ShutdownEngine()
{
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
        }
    }

    g_dialogText = text ? text : "";
    g_characterName = charName ? charName : "";

    if (charSpritePath && std::string(charSpritePath).length() > 0) {
        g_charTexture.emplace();
        if (g_charTexture->loadFromFile(charSpritePath)) {
            g_charSprite.emplace(*g_charTexture);
            g_targetCharX = charX;
            g_targetCharY = charY;
            g_hasCharacter = true;
        }
    } else {
        g_hasCharacter = false;
        g_charSprite.reset(); 
    }
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

// РЕАЛИЗАЦИЯ ВСЕХ НЕДОСТАЮЩИХ ФУНКЦИЙ ИЗ EngineWrapper.cs
EXPORT void RegisterChoiceCallback(ChoiceCallbackFn callback) { g_choiceCallback = callback; }
EXPORT void RegisterClickCallback(ClickCallbackFn callback) { g_clickCallback = callback; }
EXPORT void SetFontSize(int size) {}
EXPORT void PlayMusic(const char* trackName) {}
EXPORT void StopMusic() {}
EXPORT void SetMusicVolume(float volume) {}
EXPORT void StartMusicFadeOut(float durationSeconds) {}

// ============================================================================
// РЕНДЕР-ЦИКЛ
// ============================================================================

EXPORT bool TickEngine(float deltaTime)
{
    if (!g_window || !g_window->isOpen()) return false;

    while (const std::optional<sf::Event> event = g_window->pollEvent())
    {
        if (event->is<sf::Event::Closed>()) {
            g_window->close();
            return false;
        }

        // Клик мышкой по окну движка
        if (event->is<sf::Event::MouseButtonPressed>()) {
            auto mouseEvent = event->getIf<sf::Event::MouseButtonPressed>();
            if (mouseEvent && mouseEvent->button == sf::Mouse::Button::Left) {
               
                if (g_clickCallback) g_clickCallback();
            }
            // Клик мышкой по окну движка
        if (event->is<sf::Event::MouseButtonPressed>()) {
            auto mouseEvent = event->getIf<sf::Event::MouseButtonPressed>();
            if (mouseEvent && mouseEvent->button == sf::Mouse::Button::Left) {
                float mouseX = static_cast<float>(mouseEvent->position.x);
                float mouseY = static_cast<float>(mouseEvent->position.y);

                bool clickedChoice = false;

                // Проверяем клик по кнопкам выбора, если они есть на экране
                if (!g_choices.empty()) {
                    float choiceY = 150.0f;
                    for (size_t i = 0; i < g_choices.size(); ++i) {
                        // Прямоугольник кнопки: x от 312 до 712, y от choiceY до choiceY + 40
                        if (mouseX >= 312.0f && mouseX <= 712.0f &&
                            mouseY >= choiceY && mouseY <= choiceY + 40.0f) {
                            if (g_choiceCallback) {
                                g_choiceCallback(static_cast<int>(i));
                            }
                            clickedChoice = true;
                            break;
                        }
                        choiceY += 50.0f;
                    }
                }

                // Если кликнули не по кнопке выбора — выполняем стандартный переход по клику
                if (!clickedChoice) {
                    if (g_clickCallback) g_clickCallback();
                }
            }
        }
        }
    }

    UpdateVFX(deltaTime);

    sf::View defaultView = g_window->getDefaultView();
    sf::View activeView = defaultView;

    if (g_shakeTimer > 0.0f) {
        float offsetX = ((rand() % 100) / 100.0f - 0.5f) * 2.0f * g_shakeIntensity;
        float offsetY = ((rand() % 100) / 100.0f - 0.5f) * 2.0f * g_shakeIntensity;
        float factor = g_shakeTimer / g_shakeDuration;
        activeView.move({offsetX * factor, offsetY * factor}); 
    }
    g_window->setView(activeView);

    g_window->clear(sf::Color::Black);

    if (g_bgSprite) g_window->draw(*g_bgSprite);
    RenderCharacter(*g_window, g_targetCharX, g_targetCharY);

    sf::RectangleShape dialogBox({980.0f, 150.0f});
    dialogBox.setPosition({22.0f, 400.0f});
    dialogBox.setFillColor(sf::Color(0, 0, 0, 200));
    dialogBox.setOutlineColor(sf::Color(255, 255, 255, 100));
    dialogBox.setOutlineThickness(2.0f);
    g_window->draw(dialogBox);

   if (g_font) {
        if (!g_characterName.empty()) {
            // Исправлено: декодируем UTF-8 для имени персонажа
            sf::String utf8Name = sf::String::fromUtf8(g_characterName.begin(), g_characterName.end());
            sf::Text nameText(*g_font, utf8Name, 18); 
            nameText.setPosition({40.0f, 410.0f});
            nameText.setFillColor(sf::Color::Yellow);
            g_window->draw(nameText);
        }

        if (!g_dialogText.empty()) {
            // Исправлено: декодируем UTF-8 для текста реплики
            sf::String utf8Text = sf::String::fromUtf8(g_dialogText.begin(), g_dialogText.end());
            sf::Text mainText(*g_font, utf8Text, 16);
            mainText.setPosition({40.0f, 440.0f});
            mainText.setFillColor(sf::Color::White);
            g_window->draw(mainText);
        }

        float choiceY = 150.0f;
        for (const auto &choice : g_choices) {
            sf::RectangleShape btn({400.0f, 40.0f});
            btn.setPosition({312.0f, choiceY});
            btn.setFillColor(sf::Color(20, 20, 30, 220));
            btn.setOutlineColor(sf::Color::Cyan);
            btn.setOutlineThickness(1.0f);
            g_window->draw(btn);

            // Исправлено: декодируем UTF-8 для кнопок выбора
            sf::String utf8Choice = sf::String::fromUtf8(choice.begin(), choice.end());
            sf::Text btnText(*g_font, utf8Choice, 14);
            btnText.setPosition({330.0f, choiceY + 10.0f});
            btnText.setFillColor(sf::Color::White);
            g_window->draw(btnText);

            choiceY += 50.0f;
        }
    }

    if (g_flashTimer > 0.0f) {
        sf::RectangleShape flashOverlay({static_cast<float>(g_window->getSize().x), 
                                         static_cast<float>(g_window->getSize().y)});
        float alphaFactor = g_flashTimer / g_flashDuration;
        sf::Color currentColor = g_flashColor;
        currentColor.a = static_cast<std::uint8_t>(255 * alphaFactor);
        flashOverlay.setFillColor(currentColor);

        g_window->setView(defaultView);
        g_window->draw(flashOverlay);
    }

    g_window->display();
    return true;
}