#include <SFML/Graphics.hpp>
#include <SFML/Audio.hpp>
#include <mutex>
#include <string>
#include <cstring>
#include <memory>
#include <optional>
#include <iostream>

// Глобальные объекты движка
std::unique_ptr<sf::RenderWindow> window;
std::unique_ptr<sf::Texture> bgTexture = std::make_unique<sf::Texture>();
std::optional<sf::Sprite> bgSprite;

std::unique_ptr<sf::Texture> charTexture = std::make_unique<sf::Texture>();
std::optional<sf::Sprite> charSprite;

std::unique_ptr<sf::Font> font = std::make_unique<sf::Font>();
std::unique_ptr<sf::Text> dialogText;
std::unique_ptr<sf::Text> charNameText;

std::unique_ptr<sf::Music> music;
std::string g_basePath = "";

// Мутексы для многопоточной синхронизации
std::mutex g_dataMutex;

// Переменные плавного затухания звука
bool g_isFadingOut = false;
float g_fadeDuration = 0.0f;
float g_fadeElapsed = 0.0f;
float g_startVolume = 100.0f;
sf::Clock g_fadeClock;

extern "C" {

    __declspec(dllexport) bool InitEngine(int width, int height, const char* title, const char* basePath)
    {
        std::lock_guard<std::mutex> lock(g_dataMutex);
        g_basePath = basePath ? std::string(basePath) : "";

        window = std::make_unique<sf::RenderWindow>(sf::VideoMode({(unsigned int)width, (unsigned int)height}), sf::String::fromUtf8(title, title + std::strlen(title)));
        window->setFramerateLimit(60);

        // ИСПРАВЛЕНИЕ SFML 3: openFromFile вместо статического loadFromFile
        if (font->openFromFile(g_basePath + "Arial.ttf"))
        {
            dialogText = std::make_unique<sf::Text>(*font);
            dialogText->setCharacterSize(20);
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
        return true;
    }

    __declspec(dllexport) void UpdateScene(const char* bgName, const char* text, const char* charName, const char* charSpriteName, float charX, float charY)
    {
        std::lock_guard<std::mutex> lock(g_dataMutex);

        // 1. Обновление фона
        if (bgName != nullptr && std::strlen(bgName) > 0)
        {
            std::string path = g_basePath + std::string(bgName);
            sf::Texture tempTex;
            
            // ИСПРАВЛЕНИЕ SFML 3: loadFromFile вызывается у объекта tempTex
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

        // 2. Графическое позиционирование персонажа
        if (charSpriteName != nullptr && std::strlen(charSpriteName) > 0)
        {
            std::string path = g_basePath + std::string(charSpriteName);
            sf::Texture tempTex;
            
            // ИСПРАВЛЕНИЕ SFML 3: loadFromFile вызывается у объекта tempTex
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

        // 3. Обновление текстовых полей
        if (charNameText && charName != nullptr)
        {
            charNameText->setString(sf::String::fromUtf8(charName, charName + std::strlen(charName)));
        }

        if (dialogText && text != nullptr)
        {
            dialogText->setString(sf::String::fromUtf8(text, text + std::strlen(text)));
        }
    }

    __declspec(dllexport) bool TickEngine()
    {
        if (!window || !window->isOpen()) return false;

        // ИСПРАВЛЕНИЕ SFML 3: pollEvent возвращает std::optional
        while (const auto event = window->pollEvent())
        {
            if (event->is<sf::Event::Closed>())
            {
                window->close();
                return false;
            }
        }

        // Логика затухания музыки
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

        // Отрисовка
        {
            std::lock_guard<std::mutex> lock(g_dataMutex);
            if (bgSprite.has_value()) window->draw(*bgSprite);
            if (charSprite.has_value()) window->draw(*charSprite);
            if (charNameText) window->draw(*charNameText);
            if (dialogText) window->draw(*dialogText);
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
            music->setLooping(true); // ИСПРАВЛЕНИЕ SFML 3: setLooping вместо setLoop
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
        
        // ИСПРАВЛЕНИЕ SFML 3: Строгая типизация статуса звука
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