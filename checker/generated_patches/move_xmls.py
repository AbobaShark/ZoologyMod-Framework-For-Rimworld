#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
copy_xmls_replace.py
Запускается из папки, где лежат папки:
  Biotech, Core, Odyssey, VaniliaAnimalsExpanded

Копирует все .xml (регистр расширения не важен) в заданные целевые каталоги,
создавая их при необходимости. Если файл с таким именем уже есть — перезаписывает
и выводит в консоль уведомление о замене.
"""

from pathlib import Path
import shutil
import sys
import traceback

# --- Настройка: имена локальных папок -> целевые пути относительно корня ZoologyFramework
MAPPINGS = {
    "Biotech": Path("Zoology") / "DLC" / "Biotech" / "Patches" / "ThingDefs_Races",
    "Core": Path("Zoology") / "1.6" / "Patches" / "Core" / "ThingDefs_Races",
    "Odyssey": Path("Zoology") / "DLC" / "Odyssey" / "Patches" / "ThingDefs_Races",
    "VaniliaAnimalsExpanded": Path("Zoology") / "ModPatches" / "VanillaAnimalsExpanded" / "Patches" / "ThingDefs_Races",
}

def get_base_dir():
    """Возвращает директорию, откуда запущен скрипт (если __file__ нет — текущая рабочая)."""
    try:
        return Path(__file__).parent.resolve()
    except NameError:
        return Path.cwd().resolve()

def copy_xmls(src_dir: Path, dst_dir: Path):
    """Скопировать все .xml из src_dir в dst_dir, перезаписав при необходимости."""
    if not src_dir.exists() or not src_dir.is_dir():
        print(f"[SKIP] Исходная папка не найдена: {src_dir}")
        return

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

def main():
    base = get_base_dir()
    project_root = base.parents[1]  # .../ZoologyFramework/checker/generated_patches -> .../ZoologyFramework
    print(f"Базовая папка: {base}\n")
    print(f"Корень проекта: {project_root}\n")

    for local_name, target_rel_path in MAPPINGS.items():
        src = base / local_name
        dst = project_root / target_rel_path
        print(f"Обработка: '{local_name}' -> '{dst}'")
        copy_xmls(src, dst)

if __name__ == "__main__":
    try:
        main()
    except KeyboardInterrupt:
        print("\nПрервано пользователем.")
        sys.exit(1)
