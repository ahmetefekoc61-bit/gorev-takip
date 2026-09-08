import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

export interface TaskCommentItem {
  id: number;
  content: string;
  createdAt: string;
  userId: number;
  userFullName: string;
  userAvatarUrl: string | null;
}

@Injectable({
  providedIn: 'root'
})
export class CommentService {
  private apiUrl = 'https://localhost:7236/api/tasks';

  constructor(private http: HttpClient) { }

  getComments(taskId: number): Observable<TaskCommentItem[]> {
    return this.http.get<TaskCommentItem[]>(`${this.apiUrl}/${taskId}/comments`);
  }

  addComment(taskId: number, content: string): Observable<TaskCommentItem> {
    return this.http.post<TaskCommentItem>(`${this.apiUrl}/${taskId}/comments`, { content });
  }

  deleteComment(taskId: number, commentId: number): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/${taskId}/comments/${commentId}`);
  }
}
