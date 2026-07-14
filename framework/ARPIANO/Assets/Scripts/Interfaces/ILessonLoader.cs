// ILessonLoader.cs
// Purpose: Defines the contract for any component that can load lessons for the ARPIANO system.

using ARPIANO.Scripts.Models;

public interface ILessonLoader
{
    Lesson LoadLesson(string lessonName);
}