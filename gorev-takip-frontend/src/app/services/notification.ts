import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

export interface AppNotification {
  id: number;
  message: string;
  isRead: boolean;
  createdAt: string;

  /** Bildirimin işaret ettiği görev. Tıklanınca doğrudan o görevin detayına gidiliyor. */
  taskItemId: number | null;
}

@Injectable({
  providedIn: 'root'
})
export class NotificationService {
  private apiUrl = 'https://localhost:7236/api/notifications';

  constructor(private http: HttpClient) { }

  getNotifications(): Observable<AppNotification[]> {
    return this.http.get<AppNotification[]>(this.apiUrl);
  }

  markRead(id: number): Observable<void> {
    return this.http.post<void>(`${this.apiUrl}/${id}/read`, {});
  }

  markAllRead(): Observable<void> {
    return this.http.post<void>(`${this.apiUrl}/read-all`, {});
  }
}
