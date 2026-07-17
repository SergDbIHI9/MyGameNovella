cmake_minimum_required(VERSION 3.10)
project(EngineCore)

set(CMAKE_CXX_STANDARD 17)

set(SFML_DIR "E:/MyGameNovella/EngineCore/SFML/lib/cmake/SFML")

# ИСПРАВЛЕНИЕ: Добавлен компонент Audio в поиск!
find_package(SFML 3 REQUIRED COMPONENTS Graphics Window System Audio)

add_library(EngineCore SHARED EngineCore.cpp)

target_link_libraries(EngineCore PRIVATE SFML::Graphics SFML::Window SFML::System SFML::Audio)