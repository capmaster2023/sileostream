# Siloé Stream 1.9.5 : pilote UVC natif de la caméra HDMI (carte de capture USB branchée au téléphone)
cmake_minimum_required(VERSION 3.22.1)
project(siloeuvc C)

add_library(siloeuvc SHARED uvc.c)
target_compile_options(siloeuvc PRIVATE -O2 -Wall -Wno-unused-function)
target_link_libraries(siloeuvc log)
