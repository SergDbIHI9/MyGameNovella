#include <SFML/Graphics.hpp>
#include <SFML/Window.hpp>
#include <SFML/Audio.hpp>
#include <iostream>
#include <string>
#include <memory>
#include <optional>
#include <windows.h>
#include <cstring>
#include <fstream>
#include <mutex>

std::unique_ptr<sf::RenderWindow> window;
std::unique_ptr<sf::Font> font;
std::unique_ptr<sf::Text> dialogText;
std::unique_ptr<sf::RectangleShape> textBackground;
std::unique_ptr<sf::Texture> bgTexture;
std::optional<sf::Sprite> bgSprite;
std::unique_ptr<sf::Music> bgMusic;
std::string g_basePath = "";
std::mutex g_dataMutex;

// ПЕРЕМЕННЫЕ ДЛЯ ЗА ТУХАНИЯ И ГРОМКОСТИ
float g_userVolume = 100.f; // Громкость, которую выставил пользователь
bool g_isFadingOut = false; // Идет ли сейчас затухание?
float g_fadeSpeed = 0.f;    // Скорость уменьшения громкости за один кадр

void Log(const std::string &message)
{
    std::ofstream logFile("engine_log.txt", std::ios_base::app);
    if (logFile.is_open())
    {
        logFile << "[LOG]: " << message << std::endl;
    }
}

extern "C"
{
    __declspec(dllexport) void InitEngine(int width, int height, const char *title, const char *basePath)
    {
        Log("InitEngine: Старт");
        if (window)
            return;

        g_basePath = basePath ? std::string(basePath) : "";
        if (!g_basePath.empty() && g_basePath.back() != '\\' && g_basePath.back() != '/')
            g_basePath += "\\";

        window = std::make_unique<sf::RenderWindow>(sf::VideoMode({(unsigned int)width, (unsigned int)height}), title);

        font = std::make_unique<sf::Font>();
        if (!font->openFromFile(g_basePath + "arial.ttf"))
            Log("InitEngine: ОШИБКА шрифта!");

        dialogText = std::make_unique<sf::Text>(*font, "", 22);
        dialogText->setFillColor(sf::Color::White);
        dialogText->setPosition({40.f, (float)height - 90.f});

        textBackground = std::make_unique<sf::RectangleShape>(sf::Vector2f{(float)width - 80.f, 110.f});
        textBackground->setFillColor(sf::Color(0, 0, 0, 180));
        textBackground->setPosition({40.f, (float)height - 130.f});

        bgTexture = std::make_unique<sf::Texture>();
        bgSprite = std::nullopt;

        bgMusic = std::make_unique<sf::Music>();

        Log("InitEngine: Успешно!");
    }

    __declspec(dllexport) void UpdateScene(const char *bgName, const char *text)
    {
        std::lock_guard<std::mutex> lock(g_dataMutex);
        if (bgName != nullptr && std::strlen(bgName) > 0)
        {
            std::string path = g_basePath + std::string(bgName);
            sf::Texture tempTex;
            if (tempTex.loadFromFile(path))
            {
                *bgTexture = std::move(tempTex);
                if (!bgSprite.has_value())
                    bgSprite.emplace(*bgTexture);
                else
                    bgSprite->setTexture(*bgTexture, true);

                // АВТОМАТИЧЕСКОЕ РАСТЯГИВАНИЕ НА ВЕСЬ ЭКРАН
                if (window)
                {
                    // Получаем текущий размер окна и размер загруженной картинки
                    sf::Vector2u windowSize = window->getSize();
                    sf::Vector2u textureSize = bgTexture->getSize();

                    // Считаем, во сколько раз нужно сжать или растянуть картинку по X и Y
                    float scaleX = (float)windowSize.x / textureSize.x;
                    float scaleY = (float)windowSize.y / textureSize.y;

                    // Применяем масштаб к спрайту фона
                    bgSprite->setScale({scaleX, scaleY});
                }
            }
        }
        if (dialogText && text != nullptr)
        {
            dialogText->setString(sf::String::fromUtf8(text, text + std::strlen(text)));
        }
    }

    __declspec(dllexport) void PlayMusic(const char *trackName)
    {
        std::lock_guard<std::mutex> lock(g_dataMutex);
        if (trackName != nullptr && std::strlen(trackName) > 0)
        {
            std::string path = g_basePath + std::string(trackName);
            if (bgMusic->openFromFile(path))
            {
                g_isFadingOut = false;            // Отменяем затухание, если включили новый трек
                bgMusic->setVolume(g_userVolume); // Ставим текущую громкость из слайдера
                bgMusic->setLooping(true);
                bgMusic->play();
                Log("PlayMusic: Трек запущен");
            }
        }
    }

    __declspec(dllexport) void StopMusic()
    {
        std::lock_guard<std::mutex> lock(g_dataMutex);
        g_isFadingOut = false;
        if (bgMusic)
            bgMusic->stop();
    }

    // Изменение громкости (0.0 до 100.0)
    __declspec(dllexport) void SetMusicVolume(float volume)
    {
        std::lock_guard<std::mutex> lock(g_dataMutex);
        g_userVolume = volume;
        // Если сейчас не идет затухание, то мгновенно применяем громкость
        if (bgMusic && !g_isFadingOut)
        {
            bgMusic->setVolume(g_userVolume);
        }
    }

    // Запуск процесса плавного затухания
    __declspec(dllexport) void StartMusicFadeOut(float durationSeconds)
    {
        std::lock_guard<std::mutex> lock(g_dataMutex);
        if (!bgMusic || bgMusic->getStatus() != sf::SoundSource::Status::Playing)
            return;

        if (durationSeconds <= 0.f)
            durationSeconds = 1.0f;

        g_isFadingOut = true;
        // Вычисляем, сколько громкости отнимать каждый кадр (при ~60 кадров в секунду)
        float currentVol = bgMusic->getVolume();
        g_fadeSpeed = currentVol / (durationSeconds * 60.f);
        Log("StartMusicFadeOut: Затухание запущено");
    }

    __declspec(dllexport) bool TickEngine()
    {
        if (!window)
            return false;

        while (const std::optional<sf::Event> event = window->pollEvent())
        {
            if (event->is<sf::Event::Closed>())
            {
                window->close();
                return false;
            }
        }

        // ОБРАБОТКА ПЛАВНОГО ЗАТУХАНИЯ МУЗЫКИ КАЖДЫЙ КАДР
        {
            std::lock_guard<std::mutex> lock(g_dataMutex);
            if (g_isFadingOut && bgMusic)
            {
                float vol = bgMusic->getVolume();
                vol -= g_fadeSpeed;
                if (vol <= 0.f)
                {
                    bgMusic->stop();
                    g_isFadingOut = false;
                    bgMusic->setVolume(g_userVolume); // Возвращаем исходную громкость для будущих треков
                    Log("TickEngine: Затухание завершено, музыка остановлена");
                }
                else
                {
                    bgMusic->setVolume(vol);
                }
            }
        }

        window->clear(sf::Color(30, 30, 30));
        {
            std::lock_guard<std::mutex> lock(g_dataMutex);
            if (bgSprite.has_value())
                window->draw(*bgSprite);
            if (textBackground)
                window->draw(*textBackground);
            if (dialogText)
                window->draw(*dialogText);
        }
        window->display();
        return window->isOpen();
    }

    __declspec(dllexport) void CloseEngine()
    {
        if (bgMusic)
            bgMusic->stop();
        window.reset();
    }
}