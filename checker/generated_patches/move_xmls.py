#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
copy_xmls_replace.py
Запускается из папки, где лежат папки:
  Biotech, Core, Odyssey, VanillaAnimalsExpanded, VanillaAnimalsExpandedRoyal

Копирует все .xml (регистр расширения не важен) в заданные целевые каталоги,
создавая их при необходимости. Если файл с таким именем уже есть — перезаписывает
и выводит в консоль уведомление о замене.
"""

from pathlib import Path
import shutil
import sys
import traceback
import os

# --- Настройка: имена локальных папок -> целевые пути относительно корня ZoologyFramework
MAPPINGS = {
    "Biotech": Path("Zoology") / "DLC" / "Biotech" / "Patches" / "ThingDefs_Races",
    "Core": Path("Zoology") / "1.6" / "Patches" / "Core" / "ThingDefs_Races",
    "Odyssey": Path("Zoology") / "DLC" / "Odyssey" / "Patches" / "ThingDefs_Races",
    "VanillaAnimalsExpanded": Path("Zoology") / "ModPatches" / "VanillaAnimalsExpanded" / "Patches" / "ThingDefs_Races",
    "VanillaAnimalsExpandedRoyal": Path("Zoology") / "ModPatches" / "VanillaAnimalsExpandedRoyal" / "Patches" / "ThingDefs_Races",
    "VanillaAnimalsExpandedEndangered": Path("Zoology") / "ModPatches" / "VanillaAnimalsExpandedEndangered" / "Patches" / "ThingDefs_Races",
    "VanillaAnimalsExpandedWasteland": Path("Zoology") / "ModPatches" / "VanillaAnimalsExpandedWasteland" / "Patches" / "ThingDefs_Races",
    "AlphaAnimals": Path("Zoology") / "ModPatches" / "AlphaAnimals" / "Patches" / "ThingDefs_Races",
}

def get_base_dir():
    """Возвращает директорию, откуда запущен скрипт (если __file__ нет — текущая рабочая)."""
    try:
        return Path(__file__).parent.resolve()
    except NameError:
        return Path.cwd().resolve()

def find_project_root(base: Path) -> Path:
    """
    Находит корень ZoologyFramework (где есть папки Zoology и checker).
    Если не найден, оставляет прежнее поведение через base.parents[1].
    """
    for parent in [base, *base.parents]:
        if (parent / "Zoology").is_dir() and (parent / "checker").is_dir():
            return parent

    if len(base.parents) > 1:
        return base.parents[1]
    return base.parent

def copy_xmls(src_dir: Path, dst_dir: Path):
    """Скопировать все .xml из src_dir в dst_dir, перезаписав при необходимости."""
    if not src_dir.exists() or not src_dir.is_dir():
        print(f"[SKIP] Исходная папка не найдена: {src_dir}")
        return 0, 0

    # Создаем папку-приёмник (включая родительские)
    dst_dir.mkdir(parents=True, exist_ok=True)

    files_copied = 0
    files_replaced = 0
    for p in src_dir.iterdir():
        if p.is_file() and p.suffix.lower() == ".xml":
            dest = dst_dir / p.name
            try:
                replacing = dest.exists()
                # copy2 сохраняет метаданные когда возможно
                shutil.copy2(str(p), str(dest))
                files_copied += 1
                if replacing:
                    files_replaced += 1
                    print(f"[REPLACED] {p.name} -> {dest}")
                else:
                    print(f"[COPIED]   {p.name} -> {dest}")
            except Exception:
                print(f"[ERROR]   {p.name} -> {dest}")
                traceback.print_exc()
    print(f"Итого в '{src_dir.name}': скопировано {files_copied} файлов, заменено {files_replaced}.\n")
    return files_copied, files_replaced

def launched_by_double_click() -> bool:
    """
    Пытается определить запуск двойным кликом в Windows:
    если к консоли привязан только текущий процесс, обычно это отдельное окно python.exe.
    """
    if os.name != "nt":
        return False

    try:
        import ctypes

        process_list = (ctypes.c_uint * 2)()
        count = ctypes.windll.kernel32.GetConsoleProcessList(process_list, 2)
        return count <= 1
    except Exception:
        return False

def maybe_pause():
    """
    Пауза нужна в основном для запуска двойным кликом:
      --pause    : всегда пауза
      --no-pause : никогда пауза
      по умолчанию: авто (только при двойном клике)
    """
    args = {arg.strip().lower() for arg in sys.argv[1:]}
    if "--no-pause" in args:
        return
    if "--pause" in args or launched_by_double_click():
        try:
            input("\nГотово. Нажмите Enter для выхода...")
        except EOFError:
            pass

def main():
    base = get_base_dir()
    project_root = find_project_root(base)
    print(f"Базовая папка: {base}\n")
    print(f"Корень проекта: {project_root}\n")

    total_copied = 0
    total_replaced = 0

    for local_name, target_rel_path in MAPPINGS.items():
        src = base / local_name
        dst = project_root / target_rel_path
        print(f"Обработка: '{local_name}' -> '{dst}'")

        copied, replaced = copy_xmls(src, dst)
        total_copied += copied
        total_replaced += replaced

    print(f"ИТОГО: скопировано {total_copied} файлов, заменено {total_replaced}.")

if __name__ == "__main__":
    try:
        main()
    except KeyboardInterrupt:
        print("\nПрервано пользователем.")
        sys.exit(1)
    finally:
        maybe_pause()
