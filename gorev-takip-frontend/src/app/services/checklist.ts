import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { ChecklistItem } from './task';

/**
 * Görevin alt adımları. Bir görevin beş adımı için beş ayrı görev açıldığında aralarındaki
 * bağ kayboluyordu; kontrol listesi adımları görevin içinde tutuyor.
 */
@Injectable({ providedIn: 'root' })
export class ChecklistService {
  private apiUrl = 'https://localhost:7236/api/tasks';

  constructor(private http: HttpClient) { }

  getItems(taskId: number): Observable<ChecklistItem[]> {
    return this.http.get<ChecklistItem[]>(`${this.apiUrl}/${taskId}/checklist`);
  }

  addItem(taskId: number, text: string): Observable<ChecklistItem> {
    return this.http.post<ChecklistItem>(`${this.apiUrl}/${taskId}/checklist`, { text });
  }

  /** Yalnızca gönderilen alan güncellenir; işaretleme ve metin değişikliği ayrı yetkilere tabi. */
  updateItem(taskId: number, id: number, changes: { text?: string; isDone?: boolean }): Observable<void> {
    return this.http.put<void>(`${this.apiUrl}/${taskId}/checklist/${id}`, changes);
  }

  deleteItem(taskId: number, id: number): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/${taskId}/checklist/${id}`);
  }

  reorder(taskId: number, orderedIds: number[]): Observable<void> {
    return this.http.patch<void>(`${this.apiUrl}/${taskId}/checklist/reorder`, { orderedIds });
  }
}
