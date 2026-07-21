#include <SFML/Graphics.hpp>
#include <SFML/Audio.hpp>
#include <mutex>
#include <string>
#include <cstring>
#include <memory>
#include <optional>
#include <iostream>
#include <sstream>

// Глобальные объекты движка
std::unique_ptr<sf::RenderWindow> window;
std::unique_ptr<sf::Texture> bgTexture = std::make_unique<sf::Texture>();
std::optional<sf::Sprite> bgSprite;

std::unique_ptr<sf::Texture> charTexture = std::make_unique<sf::Texture>();
std::optional<sf::Sprite> charSprite;

std::unique_ptr<sf::Font> font = std::make_unique<sf::Font>();
std::unique_ptr<sf::Text> dialogText;
std::unique_ptr<sf::Text> charNameText;

// Элементы выборов
std::optional<sf::Text> choiceTexts[4];
std::optional<sf::RectangleShape> choiceBoxes[4];
int activeChoiceCount = 0;

std::unique_ptr<sf::Music> music;
std::string g_basePath = "";
std::string g_currentRawText = "";
unsigned int g_fontSize = 20;

// Синхронизация и аудо-затухание
std::mutex g_dataMutex;
bool g_isFadingOut = false;
float g_fadeDuration = 0.0f;
float g_fadeElapsed = 0.0f;
float g_startVolume = 100.0f;
sf::Clock g_fadeClock;

// Делегаты (Коллбэки)
typedef void(*ChoiceClickedCallback)(int choiceIndex);
typedef void(*WindowClickedCallback)();

ChoiceClickedCallback g_choiceCallback = nullptr;
WindowClickedCallback g_windowClickCallback = nullptr;

// Вспомогательная функция авто-переноса длинного текста (Word Wrap)
std::string WrapText(const std::string& input, float maxWidth, const sf::Font& fontObj, unsigned int size)
{
    if (input.empty()) return "";

    sf::Text tempText(fontObj, "", size);
    std::string result = "";
    std::string currentLine = "";
    std::istringstream words(input);
    std::string word;

    while (words >> word)
    {
        std::string testLine = currentLine.empty() ? word : currentLine + " " + word;
        tempText.setString(sf::String::fromUtf8(testLine.begin(), testLine.end()));

        if (tempText.getLocalBounds().size.x > maxWidth)
        {
            if (!result.empty()) result += "\n";
            result += currentLine;
            currentLine = word;
        }
        else
        {
            currentLine = testLine;
        }
    }
    if (!currentLine.empty())
    {
        if (!result.empty()) result += "\n";
        result += currentLine;
    }
    return result;
}

void RefreshDialogText()
{
    if (dialogText && font)
    {
        dialogText->setCharacterSize(g_fontSize);
        float maxWidth = window ? (window->getSize().x - 100.f) : 900.f;
        std::string wrapped = WrapText(g_currentRawText, maxWidth, *font, g_fontSize);
        dialogText->setString(sf::String::fromUtf8(wrapped.begin(), wrapped.end()));
    }
}

extern "C" {

    __declspec(dllexport) void RegisterChoiceCallback(ChoiceClickedCallback callback)
    {
        std::lock_guard<std::mutex> lock(g_dataMutex);
        g_choiceCallback = callback;
    }

    __declspec(dllexport) void RegisterClickCallback(WindowClickedCallback callback)
    {
        std::lock_guard<std::mutex> lock(g_dataMutex);
        g_windowClickCallback = callback;
    }

    __declspec(dllexport) void SetFontSize(int size)
    {
        std::lock_guard<std::mutex> lock(g_dataMutex);
        if (size >= 12 && size <= 48)
        {
            g_fontSize = static_cast<unsigned int>(size);
            RefreshDialogText();
        }
    }

    __declspec(dllexport) void UpdateChoices(const char* c1, const char* c2, const char* c3, const char* c4)
    {
        std::lock_guard<std::mutex> lock(g_dataMutex);
        const char* choices[4] = { c1, c2, c3, c4 };
        activeChoiceCount = 0;

        if (!window || !font) return;
        sf::Vector2u winSize = window->getSize();

        float startY = winSize.y * 0.3f;
        float boxHeight = 50.f;
        float spacing = 20.f;

        for (int i = 0; i < 4; ++i)
        {
            if (choices[i] != nullptr && std::strlen(choices[i]) > 0)
            {
                sf::RectangleShape box;
                box.setSize({winSize.x * 0.6f, boxHeight});
                box.setFillColor(sf::Color(20, 20, 25, 230));
                box.setOutlineColor(sf::Color(0, 120, 215, 100));
                box.setOutlineThickness(2.f);
                box.setOrigin({box.getSize().x / 2.f, box.getSize().y / 2.f});
                box.setPosition({winSize.x / 2.f, startY + (i * (boxHeight + spacing))});
                choiceBoxes[i] = box;

                sf::Text txt(*font);
                txt.setString(sf::String::fromUtf8(choices[i], choices[i] + std::strlen(choices[i])));
                txt.setCharacterSize(22);
                txt.setFillColor(sf::Color::White);

                sf::FloatRect textRect = txt.getLocalBounds();
                txt.setOrigin({textRect.position.x + textRect.size.x / 2.0f, textRect.position.y + textRect.size.y / 2.0f});
                txt.setPosition({winSize.x / 2.f, startY + (i * (boxHeight + spacing))});

                choiceTexts[i] = txt;
                activeChoiceCount++;
            }
            else
            {
                choiceBoxes[i].reset();
                choiceTexts[i].reset();
            }
        }
    }

    __declspec(dllexport) void InitEngine(int width, int height, const char* title, const char* basePath)
    {
        std::lock_guard<std::mutex> lock(g_dataMutex);
        g_basePath = basePath ? std::string(basePath) : "";

        window = std::make_unique<sf::RenderWindow>(sf::VideoMode({(unsigned int)width, (unsigned int)height}), sf::String::fromUtf8(title, title + std::strlen(title)));
        window->setFramerateLimit(60);

        if (font->openFromFile(g_basePath + "Arial.ttf"))
        {
            dialogText = std::make_unique<sf::Text>(*font);
            dialogText->setCharacterSize(g_fontSize);
            dialogText->setFillColor(sf::Color::White);
            dialogText->setPosition({50.f, 470.f});

            charNameText = std::make_unique<sf::Text>(*font);
            charNameText->setCharacterSize(24);
            charNameText->setFillColor(sf::Color::Yellow);
            charNameText->setPosition({50.f, 430.f});
        }
        else
        {
            std::cerr << "Не удалось загрузить шрифт Arial.ttf из " << g_basePath << std::endl;
        }

        music = std::make_unique<sf::Music>();
    }

    __declspec(dllexport) void UpdateScene(const char* bgName, const char* text, const char* charName, const char* charSpriteName, float charX, float charY)
    {
        std::lock_guard<std::mutex> lock(g_dataMutex);

        // 1. Фон
        if (bgName != nullptr && std::strlen(bgName) > 0)
        {
            std::string path = g_basePath + std::string(bgName);
            sf::Texture tempTex;

            if (tempTex.loadFromFile(path))
            {
                *bgTexture = std::move(tempTex);
                if (!bgSprite.has_value()) bgSprite.emplace(*bgTexture);
                else bgSprite->setTexture(*bgTexture);

                if (window)
                {
                    sf::Vector2u windowSize = window->getSize();
                    sf::Vector2u textureSize = bgTexture->getSize();
                    bgSprite->setScale({(float)windowSize.x / textureSize.x, (float)windowSize.y / textureSize.y});
                }
            }
        }
        else
        {
            bgSprite.reset();
        }

        // 2. Персонаж
        if (charSpriteName != nullptr && std::strlen(charSpriteName) > 0)
        {
            std::string path = g_basePath + std::string(charSpriteName);
            sf::Texture tempTex;

            if (tempTex.loadFromFile(path))
            {
                *charTexture = std::move(tempTex);
                if (!charSprite.has_value()) charSprite.emplace(*charTexture);
                else charSprite->setTexture(*charTexture);

                if (window)
                {
                    sf::Vector2u windowSize = window->getSize();
                    sf::Vector2u textureSize = charTexture->getSize();

                    float targetHeight = windowSize.y * 0.75f;
                    float scale = targetHeight / textureSize.y;
                    charSprite->setScale({scale, scale});
                    charSprite->setOrigin({(float)textureSize.x / 2.0f, (float)textureSize.y});

                    float finalX = windowSize.x * (charX / 100.0f);
                    float finalY = windowSize.y * (charY / 100.0f);
                    charSprite->setPosition({finalX, finalY});
                }
            }
        }
        else
        {
            charSprite.reset();
        }

        // 3. Имя и Динамический перенос текста
        if (charNameText && charName != nullptr)
        {
            charNameText->setString(sf::String::fromUtf8(charName, charName + std::strlen(charName)));
        }

        if (text != nullptr)
        {
            g_currentRawText = std::string(text);
            RefreshDialogText();
        }
    }

    __declspec(dllexport) bool TickEngine()
    {
        if (!window || !window->isOpen()) return false;

        while (const auto event = window->pollEvent())
        {
            if (event->is<sf::Event::Closed>())
            {
                window->close();
                return false;
            }

            if (const auto* mouseBtn = event->getIf<sf::Event::MouseButtonPressed>())
            {
                if (mouseBtn->button == sf::Mouse::Button::Left)
                {
                    sf::Vector2f mousePos(static_cast<float>(mouseBtn->position.x), static_cast<float>(mouseBtn->position.y));
                    bool choiceClicked = false;

                    // Клик по выборам
                    for (int i = 0; i < 4; ++i)
                    {
                        if (choiceBoxes[i].has_value() && choiceBoxes[i]->getGlobalBounds().contains(mousePos))
                        {
                            if (g_choiceCallback)
                            {
                                g_choiceCallback(i);
                            }
                            choiceClicked = true;
                            break;
                        }
                    }

                    // Если клик был мимо выборов — переключаем сценарий
                    if (!choiceClicked && g_windowClickCallback)
                    {
                        g_windowClickCallback();
                    }
                }
            }
        }

        if (g_isFadingOut && music)
        {
            g_fadeElapsed = g_fadeClock.getElapsedTime().asSeconds();
            if (g_fadeElapsed >= g_fadeDuration)
            {
                music->stop();
                g_isFadingOut = false;
            }
            else
            {
                float ratio = 1.0f - (g_fadeElapsed / g_fadeDuration);
                music->setVolume(g_startVolume * ratio);
            }
        }

        window->clear(sf::Color::Black);

        {
            std::lock_guard<std::mutex> lock(g_dataMutex);
            if (bgSprite.has_value()) window->draw(*bgSprite);
            if (charSprite.has_value()) window->draw(*charSprite);
            if (charNameText) window->draw(*charNameText);
            if (dialogText) window->draw(*dialogText);

            for (int i = 0; i < 4; ++i)
            {
                if (choiceBoxes[i].has_value()) window->draw(*choiceBoxes[i]);
                if (choiceTexts[i].has_value()) window->draw(*choiceTexts[i]);
            }
        }

        window->display();
        return true;
    }

    __declspec(dllexport) void CloseEngine()
    {
        std::lock_guard<std::mutex> lock(g_dataMutex);
        if (window && window->isOpen())
        {
            window->close();
        }
    }

    __declspec(dllexport) void PlayMusic(const char* musicName)
    {
        std::lock_guard<std::mutex> lock(g_dataMutex);
        if (!music || !musicName || std::strlen(musicName) == 0) return;

        g_isFadingOut = false;
        std::string path = g_basePath + std::string(musicName);
        if (music->openFromFile(path))
        {
            music->setLooping(true);
            music->setVolume(100.f);
            music->play();
        }
    }

    __declspec(dllexport) void StopMusic()
    {
        std::lock_guard<std::mutex> lock(g_dataMutex);
        if (music)
        {
            g_isFadingOut = false;
            music->stop();
        }
    }

    __declspec(dllexport) void SetMusicVolume(float volume)
    {
        std::lock_guard<std::mutex> lock(g_dataMutex);
        if (music && !g_isFadingOut)
        {
            music->setVolume(volume);
        }
    }

    __declspec(dllexport) void StartMusicFadeOut(float duration)
    {
        std::lock_guard<std::mutex> lock(g_dataMutex);

        if (music && music->getStatus() == sf::SoundSource::Status::Playing)
        {
            g_startVolume = music->getVolume();
            g_fadeDuration = duration;
            g_fadeElapsed = 0.0f;
            g_isFadingOut = true;
            g_fadeClock.restart();
        }
    }
}